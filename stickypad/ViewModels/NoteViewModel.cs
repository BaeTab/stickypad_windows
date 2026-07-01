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

        _saver = new DebounceAction(SaveDelay, _ => SaveAsync());
        _hydrating = false;
    }

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
        Title = TextExtraction.DeriveTitle(plain);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save note {NoteId}", Note.Id);
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
