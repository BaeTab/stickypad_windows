using System;
using System.Collections.Generic;
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
        var window = new NoteWindow(vm, OnWindowDismissed, CreateAndShowNew, OpenNotesList, FocusNoteByTitle);
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
