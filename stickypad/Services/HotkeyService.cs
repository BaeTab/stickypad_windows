using System;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NHotkey;
using NHotkey.Wpf;

namespace StickyPad.Services;

public sealed class HotkeyService : IHotkeyService
{
    private const string NewNoteId = "StickyPad.NewNote";
    private const string NotesListId = "StickyPad.NotesList";

    private readonly ILogger<HotkeyService> _logger;
    private Action? _newNoteHandler;
    private Action? _notesListHandler;
    private bool _registered;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
    }

    public void Register(Action newNoteHandler, Action openNotesListHandler)
    {
        Unregister();
        _newNoteHandler = newNoteHandler;
        _notesListHandler = openNotesListHandler;

        try
        {
            HotkeyManager.Current.AddOrReplace(NewNoteId, Key.N, ModifierKeys.Control | ModifierKeys.Shift, OnNewNote);
            HotkeyManager.Current.AddOrReplace(NotesListId, Key.L, ModifierKeys.Control | ModifierKeys.Shift, OnNotesList);
            _registered = true;
        }
        catch (HotkeyAlreadyRegisteredException ex)
        {
            // Another app already owns the binding — log and skip; user can disable global hotkeys.
            _logger.LogWarning(ex, "Hotkey conflict for {Name}", ex.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register global hotkeys");
        }
    }

    public void Unregister()
    {
        if (!_registered) return;
        try
        {
            HotkeyManager.Current.Remove(NewNoteId);
            HotkeyManager.Current.Remove(NotesListId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unregister hotkeys");
        }
        _registered = false;
    }

    private void OnNewNote(object? sender, HotkeyEventArgs e)
    {
        e.Handled = true;
        _newNoteHandler?.Invoke();
    }

    private void OnNotesList(object? sender, HotkeyEventArgs e)
    {
        e.Handled = true;
        _notesListHandler?.Invoke();
    }
}
