using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StickyPad.Models;
using StickyPad.Resources;
using StickyPad.Utils;
using StickyPad.ViewModels;
using StickyPad.Views;

namespace StickyPad.Services;

public sealed class WindowManager : IWindowManager
{
    private readonly INoteRepository _repository;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WindowManager> _logger;
    private readonly List<NoteWindow> _windows = new();
    private NotesListWindow? _notesListWindow;
    private SettingsWindow? _settingsWindow;

    public WindowManager(INoteRepository repository, IServiceProvider services, ILoggerFactory loggerFactory)
    {
        _repository = repository;
        _services = services;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<WindowManager>();
    }

    public async Task RestoreAllAsync()
    {
        var notes = await _repository.GetAllAsync().ConfigureAwait(true);

        if (notes.Count == 0)
        {
            CreateAndShowNew();
            return;
        }

        foreach (var note in notes)
        {
            var window = BuildWindow(note);
            if (!note.IsHidden) window.Show();
        }
    }

    public async Task ReloadAsync()
    {
        // Close every existing live window, then re-build from the database. Used after import/restore.
        foreach (var window in _windows.ToList()) window.RequestClose();
        _windows.Clear();
        await RestoreAllAsync().ConfigureAwait(true);
    }

    public NoteWindow CreateAndShowNew()
    {
        var note = new Note
        {
            X = 120 + _windows.Count * 24,
            Y = 120 + _windows.Count * 24,
            Width = 280,
            Height = 280,
            Color = (NoteColor)(_windows.Count % NotePalette.All.Count),
        };
        _ = _repository.UpsertAsync(note);
        var window = BuildWindow(note);
        window.Show();
        window.Activate();
        return window;
    }

    public NoteWindow CreateAndShowNew(NoteTemplate template)
    {
        // 시작 시 자동 생성됐거나 아직 손대지 않은 빈 노트가 있으면 정리 — 템플릿 노트 옆에
        // 빈 메모가 덩그러니 하나 더 남지 않도록.
        DiscardEmptyPlaceholders();

        var content = template.Content.Replace("{{date}}", DateTime.Now.ToString("yyyy-MM-dd"));
        var plainText = TextExtraction.ToPlainText(content, template.Format);
        var note = new Note
        {
            X = 120 + _windows.Count * 24,
            Y = 120 + _windows.Count * 24,
            Width = 280,
            Height = 280,
            Color = template.Color,
            Content = content,
            Format = template.Format,
            PlainText = plainText,
            Title = TextExtraction.DeriveTitle(plainText),
            Tags = TextExtraction.ExtractTags(plainText),
        };
        _ = _repository.UpsertAsync(note);
        var window = BuildWindow(note);
        window.Show();
        window.Activate();
        return window;
    }

    public void ShowAll()
    {
        foreach (var w in _windows)
        {
            if (!w.IsVisible) w.Show();
        }
    }

    public void HideAll()
    {
        foreach (var w in _windows)
        {
            if (w.IsVisible) w.Hide();
        }
    }

    public void ToggleAllVisible()
    {
        if (_windows.Count == 0)
        {
            CreateAndShowNew();
            return;
        }
        if (_windows.Any(w => w.IsVisible)) HideAll(); else ShowAll();
    }

    public void OpenNotesList()
    {
        if (_notesListWindow is null)
        {
            var vm = _services.GetRequiredService<NotesListViewModel>();
            _notesListWindow = new NotesListWindow(vm);
        }
        _ = _notesListWindow.ShowAndReloadAsync();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        var vm = _services.GetRequiredService<SettingsViewModel>();
        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public async Task<NoteWindow?> OpenFileAsync(string path)
    {
        var full = LinkedFile.Normalize(path);
        if (string.IsNullOrEmpty(full)) return null;

        if (!File.Exists(full))
        {
            MessageBox.Show(string.Format(Strings.Note_FileNotFoundFormat, full), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        // 이미 열려 있는 연동 창이면 그 창을 앞으로.
        var live = _windows.FirstOrDefault(w =>
            string.Equals(w.ViewModel.LinkedFilePath, full, StringComparison.OrdinalIgnoreCase));
        if (live is not null)
        {
            ShowAndActivate(live);
            return live;
        }

        string content;
        try
        {
            (content, _) = await LinkedFile.ReadAsync(full).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open linked file {Path}", full);
            MessageBox.Show(string.Format(Strings.Note_OpenFileError, ex.Message), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var format = LinkedFile.FormatFor(full);
        var fileName = Path.GetFileName(full);

        // 같은 파일에 연동된 노트가 DB 에 이미 있으면 재사용(파일이 원본이므로 내용은 파일로 갱신).
        var note = await _repository.FindByLinkedPathAsync(full).ConfigureAwait(true);
        if (note is null)
        {
            note = new Note
            {
                X = 140 + _windows.Count * 24,
                Y = 140 + _windows.Count * 24,
                Width = 380,
                Height = 460,
                Color = NoteColor.Blue,
                LinkedFilePath = full,
            };
        }

        note.LinkedFilePath = full;
        note.Content = content;
        note.Format = format;
        note.PlainText = TextExtraction.ToPlainText(content, format);
        note.Title = fileName;
        note.Tags = TextExtraction.ExtractTags(note.PlainText);
        await _repository.UpsertAsync(note).ConfigureAwait(true);

        // 파일을 여는 순간, 곁에 있던 빈 자리표시 노트는 정리한다(빈 메모가 하나 더 남지 않도록).
        DiscardEmptyPlaceholders();

        var window = BuildWindow(note);
        window.OpenInPreview = true;   // .md 를 열면 렌더링된 화면으로 먼저 보여준다.
        window.Show();
        window.Activate();
        return window;
    }

    public bool FocusNoteByTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var live = _windows.FirstOrDefault(w =>
            string.Equals(w.ViewModel.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));
        if (live is not null)
        {
            ShowAndActivate(live);
            return true;
        }

        var note = _repository.FindByTitleAsync(title).GetAwaiter().GetResult();
        if (note is null) return false;
        var window = BuildWindow(note);
        window.Show();
        window.Activate();
        return true;
    }

    public bool FocusNoteById(Guid id)
    {
        var live = _windows.FirstOrDefault(w => w.ViewModel.Id == id);
        if (live is not null)
        {
            ShowAndActivate(live);
            return true;
        }

        var note = _repository.GetByIdAsync(id).GetAwaiter().GetResult();
        if (note is null) return false;
        var window = BuildWindow(note);
        window.Show();
        window.Activate();
        return true;
    }

    /// 볼트 감시가 계산한 외부 변경 diff 를 열린 창에 세밀 반영한다(spec-5 §3.7).
    /// UI 스레드 전용 — 감시자가 Dispatcher 로 넘겨 호출한다. ReloadAsync(전 창 폐기)와 달리
    /// 해당 노트 창만 갱신/생성/정리하므로 무관한 창은 깜빡이지 않는다.
    public async Task ApplyVaultDiffAsync(VaultDiff diff)
    {
        // 변경: 미저장 편집 없으면 조용히 교체, 있으면(또는 WYSIWYG 편집 중) 파일 우선/내 편집 유지 프롬프트.
        foreach (var (fresh, oldContent) in diff.Changed)
        {
            var win = _windows.FirstOrDefault(w => w.ViewModel.Id == fresh.Id);
            if (win is null) continue;   // 창이 없으면 캐시 갱신만으로 충분(다음 열람 때 최신)

            var dirty = win.IsWysiwygActive
                || !VaultContentEquals(win.ViewModel.Content, oldContent);
            if (dirty)
            {
                var choice = MessageBox.Show(win,
                    Strings.Note_FileChangedPrompt, Strings.Note_FileChangedTitle,
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes) continue;   // 내 편집 유지 → 다음 저장이 파일을 되덮음
            }
            await win.ApplyExternalContentAsync(fresh.Content, fresh.Format).ConfigureAwait(true);
        }

        // 추가: 창만 만들고 Show 하지 않는다(§2.1 — 깜짝 팝업·창 폭탄 방지). 알림은 감시자가 1건 보냄.
        foreach (var note in diff.Added)
        {
            if (note.IsDeleted) continue;
            if (_windows.Any(w => w.ViewModel.Id == note.Id)) continue;
            BuildWindow(note);
        }

        // 삭제: 미저장 편집 없으면 조용히 닫기(파일이 원본). 있으면 "다시 저장할까요?" 프롬프트.
        foreach (var (id, oldContent) in diff.Removed)
        {
            var win = _windows.FirstOrDefault(w => w.ViewModel.Id == id);
            if (win is null) continue;
            var vm = win.ViewModel;

            var dirty = win.IsWysiwygActive
                || !VaultContentEquals(vm.Content, oldContent);
            if (dirty)
            {
                var choice = MessageBox.Show(win,
                    Strings.Vault_FileDeletedPrompt, Strings.Note_FileChangedTitle,
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
                if (choice == MessageBoxResult.Yes)
                {
                    // 유지: SaveContentAsync 는 캐시에 없는 id 를 무시하므로 Upsert 로 재등록 —
                    // 파일이 새로 만들어지며 노트가 볼트에 복귀한다.
                    vm.Note.Content = vm.Content;
                    vm.Note.Format = vm.Format;
                    vm.Note.PlainText = vm.PlainText;
                    vm.Note.Title = vm.Title;
                    vm.Note.Tags = TextExtraction.ExtractTags(vm.PlainText);
                    await _repository.UpsertAsync(vm.Note).ConfigureAwait(true);
                    continue;
                }
            }

            vm.SuspendFileSync();   // 닫힘 flush 가 연동 파일을 건드리지 않게
            win.RequestClose();
            _windows.Remove(win);
            vm.Dispose();
        }

        if (_notesListWindow?.IsVisible == true)
        {
            _ = _notesListWindow.ShowAndReloadAsync();
        }
    }

    public bool TryGetLiveNoteContent(Guid id, out string content, out NoteContentFormat format)
    {
        var win = _windows.FirstOrDefault(w => w.ViewModel.Id == id);
        if (win is null)
        {
            content = string.Empty;
            format = default;
            return false;
        }
        // 디바운스 창(≤500ms) 동안 DB 는 구본 — 라이브 VM 이 최신이다.
        content = win.ViewModel.Content;
        format = win.ViewModel.Format;
        return true;
    }

    public async Task<bool> TryUpdateLiveNoteContentAsync(Guid id, string newContent)
    {
        var win = _windows.FirstOrDefault(w => w.ViewModel.Id == id);
        if (win is null) return false;
        await win.ApplyLocalEditAsync(newContent).ConfigureAwait(true);
        return true;
    }

    public void OpenQuickSwitcher()
    {
        // 목록 창과 달리 매번 새로 생성 — 팝업은 가볍고, 숨김 캐시는 stale 스냅샷·포커스 문제만 만든다.
        var vm = _services.GetRequiredService<QuickSwitcherViewModel>();
        var window = new QuickSwitcherWindow(vm);
        window.Show();
        window.Activate();
    }

    public void OpenTodoView()
    {
        if (_notesListWindow is null)
        {
            var vm = _services.GetRequiredService<NotesListViewModel>();
            _notesListWindow = new NotesListWindow(vm);
        }
        _ = _notesListWindow.ShowTodoTabAsync();
    }

    /// 미저장 편집 판정용 내용 비교 — 끝 공백·CRLF 차이는 편집으로 치지 않는다
    /// (볼트 직렬화가 어차피 정규화하는 부분이라 유령 프롬프트만 만든다).
    private static bool VaultContentEquals(string? a, string? b) =>
        string.Equals(
            (a ?? string.Empty).Replace("\r\n", "\n").TrimEnd(),
            (b ?? string.Empty).Replace("\r\n", "\n").TrimEnd(),
            StringComparison.Ordinal);

    /// 내용 없는(비연동) 노트 창을 정리한다. 노트가 하나도 없을 때 시작하면 자동으로 뜨는
    /// 빈 자리표시 노트가, 사용자가 템플릿·파일 열기로 실제 노트를 띄울 때 옆에 남지 않게 한다.
    /// 닫으면 <see cref="OnWindowDismissed"/> 가 빈 노트를 purge·제거·dispose 한다.
    private void DiscardEmptyPlaceholders()
    {
        foreach (var w in _windows.ToList())
        {
            if (w.ViewModel.IsLinkedFile || !w.ViewModel.IsEmpty) continue;
            w.RequestClose();
        }
    }

    private static void ShowAndActivate(NoteWindow window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.Editor.Focus();
    }

    private NoteWindow BuildWindow(Note note)
    {
        var corrected = MonitorHelper.EnsureOnScreen(note.X, note.Y, note.Width, note.Height);
        note.X = corrected.X;
        note.Y = corrected.Y;
        note.Width = corrected.Width;
        note.Height = corrected.Height;

        var vm = new NoteViewModel(
            note,
            _repository,
            _loggerFactory.CreateLogger<NoteViewModel>(),
            DeleteAsync);
        var window = new NoteWindow(vm, OnWindowDismissed, CreateAndShowNew, OpenNotesList, FocusNoteByTitle,
            path => OpenFileAsync(path));
        _windows.Add(window);
        return window;
    }

    private async Task DeleteAsync(NoteViewModel vm)
    {
        try
        {
            await _repository.DeleteAsync(vm.Id).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete note {NoteId}", vm.Id);
            return;
        }

        var window = _windows.Find(w => ReferenceEquals(w.ViewModel, vm));
        if (window is not null)
        {
            // 연동 노트를 지울 때 빈 내용이 원본 파일에 기록돼 파일이 비워지는 사고를 막는다.
            // 노트(DB 항목)만 휴지통으로 가고 원본 .md 파일은 그대로 둔다.
            vm.SuspendFileSync();
            vm.UpdateContent(string.Empty, NoteContentFormat.PlainText);
            window.RequestClose();
        }

        if (_notesListWindow?.IsVisible == true)
        {
            _ = _notesListWindow.ShowAndReloadAsync();
        }
    }

    private async void OnWindowDismissed(NoteWindow window)
    {
        var vm = window.ViewModel;

        try
        {
            // 빈 노트는 휴지통으로 보내지 않고 즉시 영구 삭제 — 흔적 남기지 않는다.
            if (vm.IsEmpty && !window.IsVisible)
            {
                await _repository.PurgeAsync(vm.Id).ConfigureAwait(true);
                _windows.Remove(window);
                vm.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup failed for note {NoteId}", vm.Id);
        }
    }
}
