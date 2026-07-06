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

    /// 볼트 감시가 계산한 외부 변경 diff 를 열린 창에 세밀 반영한다(UI 스레드에서만 호출).
    Task ApplyVaultDiffAsync(VaultDiff diff);

    /// 열린 창이 있으면 그 창 VM 의 (디바운스로 아직 저장 안 된 것 포함) 최신 콘텐츠를 반환.
    bool TryGetLiveNoteContent(Guid id, out string content, out Models.NoteContentFormat format);

    /// 열린 창의 에디터·VM 에 새 콘텐츠를 주입(디바운스 저장 유발). 창이 없으면 false.
    Task<bool> TryUpdateLiveNoteContentAsync(Guid id, string newContent);

    /// 빠른 전환기(Ctrl+P) 팝업을 연다.
    void OpenQuickSwitcher();

    /// 노트 목록 창을 '할 일' 탭 상태로 연다.
    void OpenTodoView();
}
