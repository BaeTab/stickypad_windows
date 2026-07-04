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

    /// 저장소: "litedb"(기본, 내장 DB) 또는 "vault"(폴더의 .md 집합). 변경은 재시작 후 적용.
    public string StorageMode { get; set; } = "litedb";

    /// 볼트 모드일 때 노트가 저장되는 폴더의 절대 경로.
    public string? VaultPath { get; set; }
}
