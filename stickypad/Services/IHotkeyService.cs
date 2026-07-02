using System;

namespace StickyPad.Services;

public interface IHotkeyService
{
    /// Stores the actions the hotkeys trigger. Call once at startup, before Apply.
    void Configure(Action newNoteHandler, Action openNotesListHandler);

    /// (Re)registers the global hotkeys from the given gesture strings, or clears them
    /// when disabled. Safe to call repeatedly (e.g. after the settings change).
    void Apply(bool enabled, string newNoteGesture, string notesListGesture);

    void Unregister();
}
