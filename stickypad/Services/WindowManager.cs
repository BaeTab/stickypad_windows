using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StickyPad.Models;
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
            MessageBox.Show($"파일을 찾을 수 없습니다:\n{full}", "StickyPad",
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
            MessageBox.Show($"파일을 열 수 없습니다:\n{ex.Message}", "StickyPad",
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
