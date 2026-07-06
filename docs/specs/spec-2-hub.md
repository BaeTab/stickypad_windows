# Spec 2 — "허브로 진화": 통합 할 일 뷰 + 빠른 전환기(Ctrl+P)

- 대상 버전: v2.2.2 → **v2.3.0** (마이너 상향 제안)
- 작성일: 2026-07-06
- 상태: 초안 (구현 전 검토용)
- 관련 코드 기준 커밋: `1a9dd0d` (main)

---

## 1. 목표 / 비목표

### 1.1 목표

| # | 기능 | 한 줄 정의 |
|---|------|-----------|
| A | **통합 할 일 뷰** | 모든 활성 노트에 흩어진 `- [ ]` / `- [x]` 체크박스 라인을 노트 목록 창의 새 "할 일" 탭에 노트별로 그룹핑해 모아 보고, 그 자리에서 체크/해제하면 원본 노트의 해당 줄이 갱신된다. 완료 항목 숨기기 토글 제공. |
| B | **빠른 전환기(Ctrl+P)** | 제목+태그 퍼지 검색 팝업. 타이핑 즉시 필터, ↑↓ Enter 로 아무 노트나 즉시 점프(`FocusNoteById`). |

### 1.2 비목표 (1차 스코프 제외)

- **RichTextXaml 노트의 체크박스**: 리치 노트의 체크박스는 텍스트가 아니라 `InlineUIContainer` 안의 실제 `CheckBox` 컨트롤이다(`NoteWindow.xaml.cs:1058-1062`). XAML 직렬화 문자열을 안전하게 부분 치환할 방법이 없고, `TextExtraction.ToPlainText` 의 보안 필터(`ContainsDangerousXaml`)를 우회하는 파싱 경로를 새로 열고 싶지 않다. → **RichTextXaml·Html 포맷 노트는 할 일 수집 대상에서 제외**. 대상은 `PlainText` / `Markdown` 두 포맷뿐.
- **코드 펜스 인식**: Markdown 코드 펜스(``` ``` ```) 내부의 `- [ ]` 텍스트도 1차에서는 할 일로 인식된다(라인 단위 단순 파싱). 실사용 빈도가 낮아 알려진 한계로 문서화만 한다.
- **전역(Global) Ctrl+P 핫키**: 아래 2.2.1 판단 참조 — 앱 내 단축키로 충분. 전역 등록은 하지 않는다.
- **할 일 항목의 인라인 텍스트 편집 / 마감일 / 정렬 커스터마이즈**: 보기+토글+점프만. 편집은 원본 노트에서.
- **휴지통 노트의 할 일**: `GetAllAsync()` 가 이미 제외하므로 자연 배제.

### 1.3 성공 기준

- 할 일 탭에서 체크 → 원본 노트 소스의 해당 줄 `[ ]`↔`[x]` **1글자만** 바뀐다(줄바꿈·들여쓰기·나머지 텍스트 바이트 단위 보존).
- 해당 노트가 열려 있으면 열린 창(소스 에디터·미리보기·WYSIWYG 모두)에 즉시 반영되고, 창의 디바운스 저장이 토글을 되돌리지 않는다.
- Ctrl+P → 타이핑 → Enter 까지 마우스 없이 노트 점프 완료.

---

## 2. UX 정의

### 2.1 통합 할 일 뷰

#### 진입 경로
1. **노트 목록 창의 세 번째 탭**: 기존 `활성 | 휴지통` 토글 옆에 `할 일 (n)` 탭 추가 (`n` = 미완료 개수). 별도 창이 아닌 탭으로 결정한 근거:
   - `NotesListWindow` 는 이미 숨김-재사용 수명주기(`ShowAndReloadAsync`, `Closing→Hide`)와 탭 토글 스타일(`TabToggle`)을 갖추고 있어 재사용 비용이 최소.
   - "허브" 컨셉상 노트 탐색과 할 일 확인이 한 창에 모이는 것이 자연스럽다.
2. **트레이 메뉴**: `모든 노트` 아래에 `할 일 보기` 항목 추가 → 목록 창을 할 일 탭 상태로 연다.

#### 화면 구성 (할 일 탭 활성 시)
- 기존 검색창(SearchBox)은 그대로 두고 **할 일 텍스트 필터**로 동작(단순 contains, 대소문자 무시).
- 태그 칩 영역·선택 도구·내보내기 메뉴는 숨김(활성 탭 전용 유지).
- 본문: 노트별 그룹 목록.
  - 그룹 헤더: 노트 색 액센트 바(기존 카드의 `AccentBrush` 재사용) + 노트 제목(무제목이면 `NoteList_Untitled`) + `미완료/전체` 카운트. 헤더 클릭 → 해당 노트로 점프(`FocusNoteById`).
  - 항목: `CheckBox`(IsChecked=완료) + 항목 텍스트. 텍스트 클릭 → 노트로 점프. 완료 항목은 취소선+회색.
- 상단 우측: `완료 숨기기` ToggleButton. 상태는 `AppSettings.TodoHideCompleted` 로 영속(기본 false).
- 빈 상태 문구: 할 일이 하나도 없으면 "체크박스(`- [ ]`)가 있는 노트가 없습니다" 안내(`EmptyMessage` 확장).

#### 갱신 시점
- 탭 진입 시·창 `ShowAndReloadAsync` 시 재수집. 라이브 워칭(노트 편집 실시간 반영)은 하지 않는다 — 탭 재진입/창 재표시로 충분(기존 목록 탭과 동일한 정책).

### 2.2 빠른 전환기 (Ctrl+P)

#### 2.2.1 전역 vs 앱 내 — 판단: **앱 내 단축키로 충분**
- StickyPad 는 트레이 상주형이라 "아무 창도 안 보이는" 상태에서의 진입은 이미 전역 `Ctrl+Shift+L`(노트 목록)이 담당한다. 전환기는 *이미 노트를 보며 작업 중일 때* 다른 노트로 점프하는 도구이므로 앱 창에 포커스가 있는 상황이 전제다.
- 전역 `Ctrl+P` 는 거의 모든 앱의 인쇄와 충돌해 NHotkey 등록 자체가 민폐이고, `HotkeyAlreadyRegisteredException` 실패 확률도 높다.
- 확장 여지: 추후 필요하면 `HotkeyService` 에 세 번째 ID(`StickyPad.QuickSwitcher`)를 추가하는 것으로 충분하도록, 진입은 전부 `IWindowManager.OpenQuickSwitcher()` 한 메서드로 수렴시킨다.

#### 진입 경로
- 모든 `NoteWindow`: `InputBindings` 에 `Ctrl+P` (기존 패턴 `NoteWindow.xaml.cs:119-130` 과 동일하게 `KeyBinding` 추가). WPF `RichTextBox` 는 Ctrl+P 기본 바인딩이 없어 충돌 없음.
- `NotesListWindow`: `PreviewKeyDown` 에서 Ctrl+P 처리(기존 Esc 처리와 같은 자리).
- 트레이 메뉴: `빠른 전환…` 항목(발견성).

#### 팝업 동작
- 외형: `WindowStyle=None`, `ShowInTaskbar=false`, `Topmost=true`, 활성 모니터 중앙 상단(세로 30% 지점), 폭 480. 상단 검색 TextBox + 결과 ListBox(최대 표시 20건, 스크롤).
- 키보드: `↑`/`↓` 선택 이동(끝에서 순환), `Enter` 열기+닫기, `Esc` 닫기, 그 외 타이핑은 검색어로. 마우스 클릭도 열기+닫기.
- 포커스 이탈(`Deactivated`) 시 자동 닫기.
- 빈 검색어: 최근 수정순(`ModifiedAt` desc) 상위 20건 — "최근 노트" 역할.
- 결과 행: 색 액센트 바 + 제목(매칭 문자 하이라이트) + 태그 라인(`#tag` 형식, 매칭 하이라이트). 기존 `HighlightedText.Segments` attached property 재사용.

#### 퍼지 매칭 규칙 (신규 `FuzzyMatcher`)
기존 `SearchMatcher` 는 토큰 단위 substring AND 매칭이라 "퍼지(비연속 부분수열)"가 아니므로 건드리지 않고, 전환기 전용 유틸을 신설한다.
- 대상 문자열: `제목 + " " + 태그라인` 합성 문자열.
- 검색어를 공백으로 토큰화, **각 토큰이 대상의 부분수열(subsequence)로 매칭**되어야 함(AND). 비교는 `OrdinalIgnoreCase`(한글은 케이스 무관이라 그대로 동작).
- 점수(내림차순 정렬):
  - 연속 매칭 run 보너스(연속 글자당 +3)
  - 단어 시작 위치 매칭 보너스(+4; 공백·`#`·구두점 뒤)
  - 제목 영역 매칭이 태그 영역 매칭보다 가중(제목 내 매칭 글자당 ×2)
  - 첫 매칭 위치가 앞일수록 보너스(−0.1×index)
  - 동점이면 `ModifiedAt` 최신순.
- 반환: `(bool Matched, double Score, IReadOnlyList<int> Positions)` — Positions 로 하이라이트 세그먼트 생성.

---

## 3. 기술 설계

### 3.1 신규 파일

| 파일 | 내용 |
|------|------|
| `stickypad/Utils/TodoExtraction.cs` | 순수 함수 유틸. 추출 + 토글. UI 의존 없음(테스트 용이). |
| `stickypad/Utils/FuzzyMatcher.cs` | 순수 함수 퍼지 매칭. |
| `stickypad/ViewModels/QuickSwitcherViewModel.cs` | 전환기 VM (`ObservableObject`, `[ObservableProperty]`/`[RelayCommand]`). |
| `stickypad/Views/QuickSwitcherWindow.xaml` + `.xaml.cs` | 팝업 창. 코드비하인드는 키 라우팅·포커스만(비즈니스 로직 금지). |

### 3.2 `TodoExtraction` — 추출과 토글

```csharp
public static class TodoExtraction
{
    public readonly record struct TodoLine(int LineIndex, bool IsChecked, string Text, string RawLine);

    /// PlainText/Markdown 소스(Content)에서 체크박스 라인 추출.
    public static IReadOnlyList<TodoLine> ExtractTasks(string source);

    /// lineIndex 줄이 expectedRaw 와 일치하면 [ ]↔[x] 마크 문자 1글자만 치환한
    /// 새 소스를 반환. 불일치(그 사이 노트가 편집됨)면 null — 호출측이 재수집.
    public static string? ToggleLine(string source, int lineIndex, string expectedRaw, bool check);
}
```

- 라인 패턴: `^(\s*)([-*+])\s+\[( |x|X)\]\s?(.*)$` (컴파일드 정규식, `TextExtraction` 의 기존 스타일과 동일하게 `RegexOptions.Compiled`).
- **추출 소스는 `Note.Content`** (PlainText 는 Content=본문, Markdown 은 Content=원시 소스). `PlainText` 프로젝션을 쓰면 안 되는 이유: Markdig 의 ToPlainText 가 불릿을 제거해 토글 시 원본 줄을 되찾을 수 없다.
- **토글은 절대 오프셋 1글자 치환**: `\n` 스캔으로 lineIndex 줄의 시작 오프셋을 찾고(줄 비교 시 꼬리 `\r` 만 제거), 정규식 매치로 마크 문자(` `/`x`)의 줄 내 위치를 구해 `source` 의 해당 절대 오프셋 한 글자만 교체한다. split/join 을 쓰지 않으므로 **CRLF/LF 혼합·말미 개행이 그대로 보존**된다.
- **낙관적 동시성**: 호출측(VM)이 추출 시점의 `RawLine` 을 들고 있다가 `ToggleLine` 에 전달. 노트가 그 사이 편집되어 줄이 달라졌으면 null → 할 일 탭 전체 재수집으로 복구(자동, 사용자에게는 체크가 되돌아간 것으로 보임).

### 3.3 토글의 영속 경로 — 열린 창과의 동기화 전략

핵심 위험: `NoteViewModel` 은 자기 메모리 사본을 500ms 디바운스로 `SaveContentAsync` 한다(`NoteViewModel.cs:19,114,175-201`). **노트가 열려 있는데 허브가 DB 에 직접 쓰면, 열린 창의 다음 flush 가 토글을 덮어쓴다.** 따라서 경로를 이원화한다. `WindowManager.ReloadAsync()`(전체 창 재구축)는 토글 한 번에 모든 창이 깜빡이는 과잉 대응이므로 쓰지 않는다 — **세밀 갱신** 채택.

#### (a) 라이브 경로 — 노트 창이 열려 있을 때
`IWindowManager` 에 2개 멤버 추가:

```csharp
/// 열린 창이 있으면 그 창 VM 의 (디바운스로 아직 저장 안 된 것 포함) 최신 콘텐츠를 반환.
bool TryGetLiveNoteContent(Guid id, out string content, out NoteContentFormat format);

/// 열린 창의 에디터·VM 에 새 콘텐츠를 주입. 창이 없으면 false.
Task<bool> TryUpdateLiveNoteContentAsync(Guid id, string newContent);
```

- 읽기도 라이브 VM 에서 해야 하는 이유: 디바운스 창(≤500ms) 동안 DB 가 구본이다.
- `TryUpdateLiveNoteContentAsync` 는 `NoteWindow` 의 신규 public 메서드 `ApplyExternalContentAsync(string text)` 를 호출한다. 이 메서드는 기존 연동 파일 외부 변경 반영 경로(`NoteWindow.xaml.cs:757-760`: `ReplaceEditorText` + `vm.UpdateContent` + 미리보기 재렌더)를 추출·재사용하고, 추가로 **WYSIWYG 모드(`_wysiwygOn`)면 `MarkdownWysiwyg.SetMarkdownAsync(text)`** 로 CodeMirror 쪽 사본까지 갱신한다(`Utils/MarkdownWysiwyg.cs:85`). `_suppressEditorSync` 가드로 에코 루프 차단.
- 영속은 자동: `vm.UpdateContent` → `Schedule()` → 디바운스 `SaveAsync` → DB + (연동 노트면) `SyncToLinkedFileAsync` 로 원본 파일까지 기록. 별도 저장 코드 불필요.
- 전제조건: 이 경로는 PlainText/Markdown 노트에서만 호출된다(RichTextXaml 은 수집 자체를 안 하므로).

#### (b) 클로즈드 경로 — 창이 없을 때
1. `INoteRepository.GetByIdAsync(id)` → `ToggleLine` → 새 Content.
2. `note.Content` 갱신 + `PlainText`/`Title`/`Tags` 재계산(`TextExtraction.ToPlainText/DeriveTitle/ExtractTags` — `WindowManager.OpenFileAsync` 의 기존 패턴 그대로).
3. `SaveContentAsync(note)` — 휴지통 상태 보존 시맨틱이 있는 기존 저장 API 를 그대로 사용(`UpsertAsync` 금지 사유는 `INoteRepository.cs:27-31` 주석과 동일).
4. **연동 노트(`LinkedFilePath != null`)면 `LinkedFile.WriteAsync(path, newContent)` 로 원본 파일도 기록**하고 반환된 WriteUtc 로 `note.LinkedFileSyncedUtc` 를 갱신한 뒤 저장. 파일이 원본인 노트에서 DB 만 고치면 다음 열람 때 파일 내용으로 되덮이기 때문. ⚠ 구현 시 `NoteRepository.SaveContentAsync` 가 `LinkedFileSyncedUtc` 를 저장 대상에 포함하는지 확인할 것(미포함이면 포함시키는 수정 동반).

#### 오케스트레이션 (NotesListViewModel 내 `ToggleTodoAsync`)
```
item 체크 클릭
 → source = TryGetLiveNoteContent(id) ?? (await repo.GetByIdAsync(id)).Content
 → newContent = TodoExtraction.ToggleLine(source, item.LineIndex, item.RawLine, newChecked)
 → null 이면: 할 일 탭 재수집(ReloadAsync) 후 종료 (stale)
 → 라이브면: await TryUpdateLiveNoteContentAsync(id, newContent)
 → 아니면: 클로즈드 경로 (b)
 → 성공 시 item.RawLine/IsChecked 로컬 갱신 (전체 재수집 없이 그 항목만)
```

### 3.4 `NotesListViewModel` / `NotesListWindow` 변경

- **뷰 모드 리팩터**: `ShowTrash` bool 하나에 탭이 2개였지만 3개가 되므로 `enum NoteListViewMode { Active, Trash, Todos }` + `[ObservableProperty] NoteListViewMode viewMode` 로 전환. 기존 XAML 바인딩 파손을 최소화하기 위해 `IsTrashView`/`ShowTrash` 는 계산 프로퍼티로 유지(파생: `ViewMode == Trash`), `IsTodoView` 추가. 탭 토글 3개는 코드비하인드 클릭 핸들러(기존 `ActiveTab_OnClick` 패턴)로 ViewMode 를 세팅.
- 추가 상태:
  - `ObservableCollection<TodoGroup> TodoGroups` — `TodoGroup(Guid NoteId, string Title, Brush AccentBrush, ObservableCollection<TodoItemViewModel> Items)`
  - `TodoItemViewModel`: `NoteId, LineIndex, RawLine, Text, [ObservableProperty] bool IsChecked` (+ 표시용 `IsVisible` 은 HideCompleted·검색어와 조합해 계산)
  - `[ObservableProperty] bool hideCompletedTodos` — 변경 시 `ISettingsService` 저장 + 목록 재필터
  - `OpenTodoCount` (탭 배지)
- 수집: `ReloadAsync` 에서 이미 로드한 `_activeRaw` 를 대상으로 `Format is PlainText or Markdown` 노트만 `TodoExtraction.ExtractTasks(note.Content)`. 별도 DB 조회 없음.
- 커맨드: `ToggleTodoCommand(TodoItemViewModel)`, `OpenTodoNoteCommand(Guid)` (=`FocusNoteById`), `ToggleHideCompletedCommand`.
- XAML: 할 일 탭 콘텐츠는 기존 ListBox 자리 Grid 에 `ItemsControl`(그룹) > `ItemsControl`(항목) 중첩으로 추가하고 `ViewMode` 로 Visibility 전환. 검색창·태그칩·선택도구의 Visibility 조건을 ViewMode 기반 컨버터로 갱신.

### 3.5 전환기 구성

- `QuickSwitcherViewModel` (DI `AddTransient`, `App.xaml.cs:121` 패턴): 열릴 때마다 `INoteRepository.GetAllAsync()` 스냅샷 로드(LiteDB 로컬이라 충분히 빠름, 활성 노트 수십 건 규모). `QueryText` 변경 → `FuzzyMatcher` 재계산 → `Results` 갱신 + `SelectedIndex=0`.
- `QuickSwitcherWindow`: 코드비하인드는 ↑↓/Enter/Esc 라우팅, `Deactivated → Close`, 열릴 때 TextBox 포커스만. 열기/점프는 VM 커맨드(`FocusNoteById` 는 `IWindowManager` 주입).
- `WindowManager.OpenQuickSwitcher()`: `_notesListWindow` 캐시 패턴과 달리 **매번 새로 생성**(팝업은 가볍고, 숨김 캐시 시 stale 스냅샷·포커스 문제만 생김). `IWindowManager` 에 `void OpenQuickSwitcher();` 추가.
- 진입 배선: `NoteWindow` InputBindings(Ctrl+P), `NotesListWindow.OnPreviewKeyDown`(Ctrl+P), `TrayService.BuildContextMenu`(`Tray_AllNotes` 다음 줄). `NoteWindow` 는 현재 콜백 델리게이트 5개를 생성자로 받는 구조(`WindowManager.BuildWindow`)이므로 `openQuickSwitcher` 콜백 1개를 같은 방식으로 추가.

### 3.6 i18n — 신규 리소스 문자열

`Strings.cs` 접근자 + `Strings.resx`(en) + `Strings.ko.resx`(ko) 3곳 모두 추가 (기존 패턴 그대로):

```
NoteList_TabTodos            할 일 / To-dos
Todo_HideCompleted           완료 숨기기 / Hide completed
Todo_EmptyMessage            체크박스(- [ ])가 있는 노트가 없습니다 / No notes contain checkboxes (- [ ])
Todo_GroupCountFormat        {0}/{1} 완료 / {0}/{1} done
Tray_TodoView                할 일 보기 / To-do view
Tray_QuickSwitcher           빠른 전환… / Quick switch…
QuickSwitcher_Placeholder    노트 이름 또는 #태그 검색… / Search note title or #tag…
QuickSwitcher_NoResults      일치하는 노트 없음 / No matching notes
```

### 3.7 설정

- `AppSettings.TodoHideCompleted` (bool, 기본 false) 추가 — additive 라 설정 파일 마이그레이션 불필요.

---

## 4. 작업 분해 + 위험도 분류

| # | 작업 | 파일 | 위험도 | 근거 |
|---|------|------|--------|------|
| 1 | `TodoExtraction` 유틸(추출+오프셋 토글) + 단위 테스트 | `Utils/TodoExtraction.cs`, `Tests.cs` | **고위험 (Opus 직접)** | 사용자 노트 원문을 변형하는 유일한 로직. 오프셋 계산 실수 = 데이터 손상. CRLF 보존·stale 검증이 correctness 핵심. |
| 2 | 토글 오케스트레이션(라이브/클로즈드/연동 3경로) + `NotesListViewModel` ViewMode 리팩터 | `ViewModels/NotesListViewModel.cs` | **고위험 (Opus 직접)** | 디바운스 저장과의 경합, 연동 파일 되덮임 방지 등 이 스펙의 동시성 설계 전체가 여기 모임. 기존 탭 상태머신 리팩터도 회귀 위험. |
| 3 | `NoteWindow.ApplyExternalContentAsync` + `IWindowManager`/`WindowManager` 라이브 read/update 멤버 | `Views/NoteWindow.xaml.cs`, `Services/IWindowManager.cs`, `Services/WindowManager.cs` | **고위험 (Opus 직접)** | 에디터 3상태(소스/미리보기/WYSIWYG) 동기화 + `_suppressEditorSync` 에코 루프 차단. WebView2 비동기 경로 포함. |
| 4 | 클로즈드 연동 노트의 `LinkedFile.WriteAsync` 반영 + `SaveContentAsync` 의 `LinkedFileSyncedUtc` 저장 여부 확인·보강 | `Services/NoteRepository.cs` 외 | **고위험 (Opus 직접)** | 원본 .md 파일을 쓰는 경로 — 실수 시 사용자 파일 손상. 기존 "빈 내용 덮어쓰기 방지" 방어선과 정합 필요. |
| 5 | `FuzzyMatcher` 유틸 + 단위 테스트 | `Utils/FuzzyMatcher.cs`, `Tests.cs` | 저위험 (Sonnet 위임) | 순수 함수, UI·저장 무관. 테스트로 완전 검증 가능. |
| 6 | `QuickSwitcherWindow` XAML/코드비하인드 + `QuickSwitcherViewModel` + DI 등록 | `Views/QuickSwitcher*`, `App.xaml.cs` | 저위험 (Sonnet 위임) | 표준 MVVM 목록 팝업. 기존 `HighlightedText`·`FocusNoteById` 재사용. |
| 7 | 할 일 탭 XAML(그룹 ItemsControl, 탭 토글, 완료 숨기기) | `Views/NotesListWindow.xaml`(+`.xaml.cs`) | 저위험 (Sonnet 위임) | 표시 전용. 로직은 #2 의 VM 커맨드 바인딩만. |
| 8 | 진입 배선: Ctrl+P InputBindings 2곳 + 트레이 메뉴 2항목 + `OpenQuickSwitcher` | `NoteWindow.xaml.cs`, `NotesListWindow.xaml.cs`, `TrayService.cs`, `WindowManager.cs` | 저위험 (Sonnet 위임) | 기존 패턴 복제 수준. 단 `NoteWindow` 생성자 시그니처 변경은 #3 머지 후 진행. |
| 9 | i18n 리소스 3종 + `AppSettings.TodoHideCompleted` | `Resources/*`, `Models/AppSettings.cs` | 저위험 (Sonnet 위임) | 기계적 추가. |
| 10 | 문서: README·CHANGELOG·버전 2.3.0 상향, 스크린샷 갱신 | `README.md`, `CHANGELOG.md`, `*.csproj`, `*.iss` | 저위험 (Sonnet 위임) | 릴리즈 전 필수 선행(전역 지침 §7). `.csproj` 와 `.iss` `AppVersion` 동기화. |

**의존 순서**: #1 → #2·#3(병렬 가능) → #4 → #7·#8 / #5 → #6 → #8 / #9·#10 은 막판.

---

## 5. 테스트 계획 (기존 107개 + 약 25~30개 추가)

기존 `stickypad.Tests/Tests.cs` 의 xUnit 스타일(단일 파일, 클래스별 영역)을 따른다.

### 5.1 `TodoExtractionTests`
- **추출**: `-`/`*`/`+` 불릿, 들여쓰기(중첩) 유지, `[x]`/`[X]` 완료 인식, `[ ]` 뒤 텍스트 없는 줄, 체크박스 아닌 일반 리스트(`- 항목`)·본문 중간 `[ ]` 미인식, 빈 소스 → 빈 결과.
- **토글 라운드트립**: toggle→untoggle 결과가 원본과 **바이트 단위 동일**. LF 전용·CRLF 전용·혼합 소스 각각에서 개행 보존. 토글 전후 diff 가 정확히 1글자.
- **stale 감지**: `expectedRaw` 불일치 → null. lineIndex 범위 밖 → null. 같은 텍스트의 줄이 여러 개여도 lineIndex 로 정확한 줄만 토글.
- **[X] 대문자 해제**: `[X]` 를 해제하면 `[ ]` 로.

### 5.2 `FuzzyMatcherTests`
- 부분수열 매칭 성립/불성립, 대소문자 무시, 한글 제목 매칭.
- 순위: 연속 매칭 > 흩어진 매칭, 단어 시작 매칭 > 중간 매칭, 제목 매칭 > 태그 매칭.
- 공백 다중 토큰 AND, 빈 쿼리 → 전건 매치(Score 0), Positions 가 하이라이트 가능한 유효 인덱스인지.

### 5.3 통합 시나리오 — VM 수준 (인메모리 LiteDB, 기존 `[Collection("LiteDb")]` 패턴)
- 클로즈드 경로: 저장된 Markdown 노트 토글 → `GetByIdAsync` 재조회 시 Content 1글자 변경 + PlainText/Tags 재계산 확인.
- RichTextXaml/Html 노트가 수집에서 제외되는지.
- 완료 숨기기 필터 + 검색어 필터 조합.

### 5.4 수동 검증 체크리스트 (UI 스레드 필요, 자동화 제외)
- 열린 노트(소스 모드) 토글 → 에디터 즉시 반영, 500ms 후 앱 재시작해도 유지(디바운스 저장 경합 없음).
- WYSIWYG 모드 창 토글 → CodeMirror 반영.
- 연동 .md 노트: 닫힌 상태 토글 → 파일 내용 변경, 파일이 빈 내용으로 덮이지 않음.
- Ctrl+P: NoteWindow/목록 창에서 열림, ↑↓ Enter Esc, 포커스 이탈 닫힘.
- ⚠ 라이브 DB 주의: 검증은 격리 데모 DB로 진행(실사용 DB·설정 교체 금지).

---

## 6. 예상 규모와 릴리즈 버전

| 항목 | 추정 |
|------|------|
| 신규 파일 | 5개 (유틸 2, VM 1, View 2) |
| 수정 파일 | 약 10개 (VM·View·Services·Resources·설정·문서) |
| 순증 LOC | 약 +1,000 ~ 1,300 (테스트 포함) |
| 신규 테스트 | 약 25~30개 (총 ≈135개) |
| 구현 공수 | 고위험 4작업이 임계 경로. 스펙 확정 후 1~2 작업일 규모 |

**버전 제안: v2.3.0** — 신규 기능 2종(하위 호환, DB 스키마 변경 없음, 설정은 additive). 릴리즈 전 CHANGELOG·README(할 일 탭·Ctrl+P 스크린샷 포함)·`stickypad_installer.iss` `AppVersion` 동기화 필수. 단일 파일 배포 검증 시 신규 자산이 없는지(전부 코드·리소스 내장) 확인 — WYSIWYG 자산 이슈(`1a9dd0d`) 재발 방지 차원에서 Release published 빌드로 스모크 테스트.
