namespace StickyPad.Models;

public sealed class AppSettings
{
    public bool AutoStartWithWindows { get; set; }
    public bool GlobalHotkeysEnabled { get; set; } = true;
    public string NewNoteHotkey { get; set; } = "Ctrl+Shift+N";
    public string NotesListHotkey { get; set; } = "Ctrl+Shift+L";
    public bool NotesHiddenAtExit { get; set; }
    public bool AutoCheckForUpdates { get; set; } = true;

    /// UI 언어: "system"(OS 따라), "en", "ko".
    public string Language { get; set; } = "system";
}
