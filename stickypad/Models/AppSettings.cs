namespace StickyPad.Models;

public sealed class AppSettings
{
    public bool AutoStartWithWindows { get; set; }
    public bool GlobalHotkeysEnabled { get; set; } = true;
    public string NewNoteHotkey { get; set; } = "Ctrl+Shift+N";
    public string NotesListHotkey { get; set; } = "Ctrl+Shift+L";
    public bool NotesHiddenAtExit { get; set; }
    public bool AutoCheckForUpdates { get; set; } = true;
}
