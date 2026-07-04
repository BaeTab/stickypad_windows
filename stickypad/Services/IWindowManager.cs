using System;
using System.Threading.Tasks;
using StickyPad.Models;
using StickyPad.Views;

namespace StickyPad.Services;

public interface IWindowManager
{
    Task RestoreAllAsync();
    Task ReloadAsync();
    NoteWindow CreateAndShowNew();

    /// 내장 템플릿으로 미리 채워진 새 노트를 만들어 보여준다.
    NoteWindow CreateAndShowNew(NoteTemplate template);

    /// 외부 .md/텍스트 파일을 열어(또는 이미 열린 연동 창을 앞으로) 렌더링해 보여준다.
    /// 노트 편집은 원본 파일에 저장되고, 파일 외부 변경은 노트로 반영된다.
    Task<NoteWindow?> OpenFileAsync(string path);

    void ShowAll();
    void HideAll();
    void ToggleAllVisible();
    void OpenNotesList();
    void OpenSettings();
    bool FocusNoteByTitle(string title);
    bool FocusNoteById(Guid id);
}
