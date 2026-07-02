using System;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NHotkey;
using NHotkey.Wpf;
using StickyPad.Utils;

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

    public void Configure(Action newNoteHandler, Action openNotesListHandler)
    {
        _newNoteHandler = newNoteHandler;
        _notesListHandler = openNotesListHandler;
    }

    public void Apply(bool enabled, string newNoteGesture, string notesListGesture)
    {
        Unregister();
        if (!enabled) return;
        if (_newNoteHandler is null || _notesListHandler is null)
        {
            _logger.LogWarning("Hotkeys applied before Configure — nothing to bind");
            return;
        }

        TryRegister(NewNoteId, newNoteGesture, "Ctrl+Shift+N", OnNewNote);
        TryRegister(NotesListId, notesListGesture, "Ctrl+Shift+L", OnNotesList);
        _registered = true;
    }

    private void TryRegister(string id, string gesture, string fallback, EventHandler<HotkeyEventArgs> handler)
    {
        if (!HotkeyGesture.TryParse(gesture, out var key, out var mods))
        {
            _logger.LogWarning("Invalid hotkey '{Gesture}' for {Id} — falling back to {Fallback}", gesture, id, fallback);
            if (!HotkeyGesture.TryParse(fallback, out key, out mods)) return;
        }

        try
        {
            HotkeyManager.Current.AddOrReplace(id, key, mods, handler);
        }
        catch (HotkeyAlreadyRegisteredException ex)
        {
            // Another app already owns the binding — log and skip; user can pick a different one.
            _logger.LogWarning(ex, "Hotkey conflict for {Name}", ex.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register hotkey {Id} ({Gesture})", id, gesture);
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
