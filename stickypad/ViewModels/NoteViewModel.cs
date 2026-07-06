using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StickyPad.Models;
using StickyPad.Services;
using StickyPad.Utils;

namespace StickyPad.ViewModels;

public sealed partial class NoteViewModel : ObservableObject, IDisposable
{
    public static readonly IReadOnlyList<double> FontSizeCatalog =
        new double[] { 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 40 };

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly INoteRepository _repository;
    private readonly ILogger<NoteViewModel> _logger;
    private readonly DebounceAction _saver;
    private readonly Func<NoteViewModel, Task> _onDeleteRequested;
    private bool _hydrating = true;

    /// 마지막으로 파일과 동기화된 원본 텍스트. 외부 변경 감지·충돌 판단·불필요한 재저장 방지에 쓴다.
    private string _lastSyncedText;

    /// true 면 이 노트의 편집이 더 이상 연동 파일에 기록되지 않는다(삭제·연결 해제 시).
    private bool _fileSyncSuspended;

    public Guid Id => Note.Id;
    public Note Note { get; }

    public IReadOnlyList<NoteTheme> Palette => NotePalette.All;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private NoteContentFormat _format;

    [ObservableProperty]
    private string _plainText;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Theme))]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(HeaderBrush))]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    [NotifyPropertyChangedFor(nameof(ForegroundBrush))]
    [NotifyPropertyChangedFor(nameof(SubtleForegroundBrush))]
    private NoteColor _color;

    [ObservableProperty]
    private double _opacity;

    [ObservableProperty]
    private bool _isAlwaysOnTop;

    [ObservableProperty]
    private bool _isPreviewMode;

    [ObservableProperty]
    private double _editorFontSize = 14;

    public NoteTheme Theme => NotePalette.For(Color);
    public Brush BackgroundBrush => Frozen(Theme.Background);
    public Brush HeaderBrush => Frozen(Theme.Header);
    public Brush BorderBrush => Frozen(Theme.Border);
    public Brush ForegroundBrush => Frozen(Theme.Foreground);
    public Brush SubtleForegroundBrush => Frozen(Theme.SubtleForeground);

    public NoteViewModel(
        Note note,
        INoteRepository repository,
        ILogger<NoteViewModel> logger,
        Func<NoteViewModel, Task> onDeleteRequested)
    {
        Note = note;
        _repository = repository;
        _logger = logger;
        _onDeleteRequested = onDeleteRequested;

        _content = note.Content;
        _format = note.Format;
        _plainText = note.PlainText;
        _title = note.Title;
        _x = note.X;
        _y = note.Y;
        _width = note.Width;
        _height = note.Height;
        _color = note.Color;
        _opacity = ClampOpacity(note.Opacity);
        _isAlwaysOnTop = note.IsAlwaysOnTop;
        _lastSyncedText = note.Content;

        _saver = new DebounceAction(SaveDelay, _ => SaveAsync());
        _hydrating = false;
    }

    /// 외부 파일과 연동된 노트인지.
    public bool IsLinkedFile => !string.IsNullOrEmpty(Note.LinkedFilePath);

    /// 연동된 파일의 절대 경로(없으면 null).
    public string? LinkedFilePath => Note.LinkedFilePath;

    /// 마지막으로 파일과 동기화된 원본 텍스트(외부 변경 시 로컬 편집 여부 판단용).
    public string LastSyncedText => _lastSyncedText;

    /// 파일에서 다시 읽어온(또는 파일로 막 써넣은) 내용을 '동기화 기준'으로 표시.
    public void MarkSynced(string text) => _lastSyncedText = text;

    /// 이후 저장이 연동 파일을 건드리지 않도록 중단(삭제·연결 해제 시 호출).
    /// 원본 파일이 빈 내용으로 덮어써지는 사고를 막는다.
    public void SuspendFileSync() => _fileSyncSuspended = true;

    partial void OnContentChanged(string value) => Schedule();
    partial void OnFormatChanged(NoteContentFormat value) => Schedule();
    partial void OnXChanged(double value) => Schedule();
    partial void OnYChanged(double value) => Schedule();
    partial void OnWidthChanged(double value) => Schedule();
    partial void OnHeightChanged(double value) => Schedule();
    partial void OnColorChanged(NoteColor value) => Schedule();
    partial void OnOpacityChanged(double value) => Schedule();
    partial void OnIsAlwaysOnTopChanged(bool value) => Schedule();

    [RelayCommand]
    private void SetColor(NoteColor color) => Color = color;

    [RelayCommand]
    private void TogglePin() => IsAlwaysOnTop = !IsAlwaysOnTop;

    [RelayCommand]
    private void TogglePreview() => IsPreviewMode = !IsPreviewMode;

    [RelayCommand]
    private Task DeleteAsync() => _onDeleteRequested(this);

    public void UpdateContent(string content, NoteContentFormat format)
    {
        if (_hydrating) return;
        Content = content;
        Format = format;
        var plain = TextExtraction.ToPlainText(content, format);
        PlainText = plain;
        // 연동 노트의 제목은 파일 이름으로 고정 — 목록에서 어떤 파일인지 알아보기 쉽게.
        Title = IsLinkedFile
            ? System.IO.Path.GetFileName(Note.LinkedFilePath!)
            : TextExtraction.DeriveTitle(plain);
    }

    /// 볼트 감시가 반영하는 외부 변경을 저장 스케줄 없이 주입한다(hydrate) —
    /// 반영이 다시 파일 쓰기(에코)를 만들지 않도록 Schedule/UpdateContent 재진입을 차단하고,
    /// 이후 위치 저장 등이 옛 내용을 되쓰지 않도록 Note 필드까지 함께 맞춘다.
    public void HydrateExternal(string content, NoteContentFormat format)
    {
        _hydrating = true;
        try
        {
            Content = content;
            Format = format;
            var plain = TextExtraction.ToPlainText(content, format);
            PlainText = plain;
            Title = IsLinkedFile
                ? System.IO.Path.GetFileName(Note.LinkedFilePath!)
                : TextExtraction.DeriveTitle(plain);

            Note.Content = content;
            Note.Format = format;
            Note.PlainText = plain;
            Note.Title = Title;
            Note.Tags = TextExtraction.ExtractTags(plain);

            _lastSyncedText = content;   // 이 내용이 곧 파일과 동기화된 기준
        }
        finally { _hydrating = false; }
    }

    private void Schedule()
    {
        if (_hydrating) return;
        _saver.Trigger();
    }

    private async Task SaveAsync()
    {
        try
        {
            Note.Content = Content;
            Note.Format = Format;
            Note.PlainText = PlainText;
            Note.Title = Title;
            Note.Tags = TextExtraction.ExtractTags(PlainText);
            Note.X = X;
            Note.Y = Y;
            Note.Width = Width;
            Note.Height = Height;
            Note.Color = Color;
            Note.Opacity = Opacity;
            Note.IsAlwaysOnTop = IsAlwaysOnTop;
            // SaveContentAsync 는 휴지통 상태(IsDeleted/DeletedAt)를 DB 값으로 보존한다.
            // 삭제된 노트의 창이 닫히며 flush 돼도 부활하지 않도록 — UpsertAsync 를 쓰면 안 된다.
            await _repository.SaveContentAsync(Note).ConfigureAwait(false);

            await SyncToLinkedFileAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save note {NoteId}", Note.Id);
        }
    }

    /// 연동 노트라면 편집 내용을 원본 파일에 기록한다. 내용이 실제로 바뀐 경우에만 써서
    /// 위치·색상 변경 같은 저장이 파일 mtime 을 건드리지 않게 한다. 삭제/연결 해제 후에는
    /// (SuspendFileSync) 절대 파일을 건드리지 않아 원본이 빈 내용으로 덮이는 사고를 막는다.
    private async Task SyncToLinkedFileAsync()
    {
        if (!IsLinkedFile || _fileSyncSuspended) return;
        // 리치 텍스트(XAML)는 절대 텍스트 파일에 쓰지 않는다 — 연동 노트를 실수로 'T'(리치) 모드로
        // 바꿔도 원본 .md 가 XAML 로 덮이지 않도록 방어. 텍스트 소스 포맷만 파일에 반영한다.
        if (Format == NoteContentFormat.RichTextXaml) return;
        if (string.Equals(Content, _lastSyncedText, StringComparison.Ordinal)) return;

        try
        {
            var writeUtc = await LinkedFile.WriteAsync(Note.LinkedFilePath!, Content).ConfigureAwait(false);
            _lastSyncedText = Content;
            Note.LinkedFileSyncedUtc = writeUtc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write linked file {Path}", Note.LinkedFilePath);
        }
    }

    public Task FlushAsync() => _saver.FlushAsync();

    public bool IsEmpty => string.IsNullOrWhiteSpace(PlainText);

    public void Dispose() => _saver.Dispose();

    private static double ClampOpacity(double value)
    {
        if (double.IsNaN(value) || value <= 0) return 1.0;
        if (value < 0.5) return 0.5;
        if (value > 1.0) return 1.0;
        return value;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
