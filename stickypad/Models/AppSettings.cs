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

    /// 마크다운 노트를 열 때 WYSIWYG(오프라인 CodeMirror) 편집을 기본으로 켤지 여부.
    public bool PreferWysiwygMarkdown { get; set; }

    /// 할 일 탭에서 완료된 항목을 숨길지 여부. 토글 상태를 재실행 후에도 유지하기 위해 영속.
    public bool TodoHideCompleted { get; set; }
}
