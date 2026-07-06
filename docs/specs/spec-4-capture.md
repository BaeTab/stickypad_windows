# Spec 4 — 캡처 머신 (전역 빠른 캡처 · 트레이 드롭 · 이미지 붙여넣기)

- 대상 버전: v2.2.2 → **v2.3.0 제안**
- 상태: 스펙 초안 (구현 전)
- 관련 파일 기준 커밋: `1a9dd0d` (main)

---

## 1. 목표 / 비목표

### 목표
1. **전역 빠른 캡처** — 어떤 앱에서든 전역 단축키 한 번으로 작은 입력창을 띄우고, Enter 로 즉시 새 노트를 저장한다. 기본 동작은 "저장만"(노트 창을 띄우지 않음)이며 저장 후 트레이 토스트로 피드백한다. 클립보드 텍스트 자동 프리필은 옵트인 옵션.
2. **이미지 붙여넣기** — 마크다운 노트 편집기에서 클립보드 이미지를 붙여넣으면 PNG 파일로 저장하고 `![](attachments/xxx.png)` 를 캐럿 위치에 삽입한다. 미리보기(분할/전체)와 HTML/PDF 내보내기에서 해당 이미지가 실제로 보인다.
3. 위 기능이 기존 **보안 불변식**(미리보기 스크립트 OFF, 원격 네트워크 정책 불변, WYSIWYG 가상호스트 화이트리스트)을 깨지 않음을 설계 단계에서 입증한다.

### 비목표 (스코프 제외 — 근거 포함)
- **트레이 아이콘 드롭**: 기술적으로 불가 → §3.4 에서 상세 근거와 대안 제시 후 **v1 스코프 제외**를 권고한다.
- **WYSIWYG(CodeMirror 6) 내부에서의 이미지 붙여넣기**: `tools/mdeditor` 번들 재빌드 + WebMessage 프로토콜 확장이 필요해 위험 대비 효용이 낮다. WYSIWYG 는 소스 지향 편집기라 이미지가 어차피 `![](…)` 텍스트로 표시되므로, v1 은 소스 편집기(RichTextBox)에서만 붙여넣기를 지원한다. (후속 후보, §3.3.6)
- **RichText(서식) 노트의 이미지 첨부**: RichTextXaml 은 볼트 저장 시 PlainText 로 강등되는 포맷(`VaultMarkdown.cs:21-23`)이라 첨부 참조를 안정적으로 왕복할 수 없다. 기존 동작(서식 붙여넣기) 유지.
- **HTML 포맷 노트의 이미지 붙여넣기 삽입**: 미리보기 표시(src 재작성)는 지원하되, 붙여넣기 시 자동 `<img>` 삽입은 v1 제외 (마크다운만).
- **백업(JSON)·볼트 내보내기 ZIP 에 attachments 포함**: 알려진 제한으로 문서화하고 후속 릴리즈에서 처리(§3.3.7).

---

## 2. UX 정의

### 2.1 전역 빠른 캡처

#### 진입
- 새 전역 단축키 **기본값 `Ctrl+Shift+Space`** (설정에서 변경 가능, 기존 2개 단축키와 동일한 캡처 UI 사용).
  - 충돌 시 기존 `HotkeyService.TryRegister` 의 동작(로그 후 스킵, `HotkeyService.cs:58-62`)을 그대로 따른다.
- 트레이 컨텍스트 메뉴에 "빠른 캡처" 항목 추가(단축키 없이도 접근 가능).

#### 캡처 창 모양
- `QuickCaptureWindow` — 크기 약 **420×130**, 테두리 없음(`WindowStyle=None`), 둥근 모서리, 항상 위, 작업표시줄 미표시(`ShowInTaskbar=False`), **활성 모니터 상단 1/3 지점 중앙** 정렬.
- 구성: 멀티라인 `TextBox`(3~5줄, `AcceptsReturn=True`) + 하단 힌트 라벨("Enter 저장 · Shift+Enter 줄바꿈 · Esc 취소").
- WebView2 미사용 — 즉시 뜨는 가벼운 순수 WPF 창.

#### 키 동작
| 키 | 동작 |
|---|---|
| `Enter` | 저장 후 창 닫기. 기본은 노트 창을 띄우지 않음(설정으로 변경 가능) |
| `Shift+Enter` | 줄바꿈 |
| `Ctrl+Enter` | 저장 + 노트 창을 즉시 열어 이어서 편집 |
| `Esc` / 포커스 상실 | 저장 없이 닫기 (내용은 버림 — 창이 다시 뜰 때 복원하지 않음) |

- 빈 내용(공백만)으로 Enter → 아무것도 저장하지 않고 닫는다.
- 캡처 창은 **단일 인스턴스**: 이미 떠 있을 때 단축키를 다시 누르면 기존 창을 활성화한다.

#### 저장 후 피드백
- 트레이 토스트(풍선 알림): 제목 "StickyPad", 본문 `노트 저장됨 — "<파생 제목 앞 30자>"`. H.NotifyIcon `TaskbarIcon.ShowNotification` 사용, 실패 시 조용히 무시(로그만).
- 저장된 노트는 다른 노트들과 동일하게 관리 목록/검색에 즉시 반영된다.

#### 클립보드 프리필 (옵트인)
- 설정 `클립보드 텍스트로 미리 채우기` (기본 OFF).
- ON 이면 창이 뜰 때 `Clipboard.ContainsText()` 인 경우 텍스트를 **전체 선택 상태로** 프리필 — 바로 타이핑하면 대체되고, Enter 만 누르면 그대로 저장.
- 프리필 상한 100 KB(초과분 잘라냄) — 비정상적으로 큰 클립보드로 인한 UI 멈춤 방지.
- 클립보드 내용은 어떤 경로로도 로그에 남기지 않는다(기존 Serilog 정책과 동일).

#### 저장되는 노트의 형태
- `Format = Markdown` (볼트 모드 `.md` 왕복과 일치, `#태그` 자동 추출 활용).
- `Title`/`Tags`/`PlainText` 는 `WindowManager.CreateAndShowNew(template)` 와 동일하게 `TextExtraction` 으로 파생(`WindowManager.cs:83-96` 패턴 재사용).
- 위치/크기/색: 기존 캐스케이드 규칙 그대로(`120 + n*24`, 280×280, 색 순환).

### 2.2 이미지 붙여넣기

#### 트리거와 삽입
- **마크다운 노트**의 소스 편집기(RichTextBox)에서 `Ctrl+V` 시 클립보드에 이미지가 있으면(텍스트가 없을 때):
  1. 이미지를 PNG 로 attachments 폴더에 저장,
  2. 캐럿 위치에 `![](attachments/<파일명>.png)` 텍스트 삽입,
  3. 분할 미리보기가 열려 있으면 기존 디바운스 재렌더(350ms)로 즉시 표시.
- 클립보드에 텍스트와 이미지가 **둘 다** 있으면 기존대로 텍스트 우선(현행 `OnEditorPasting` 분기 유지).
- (P2, 선택) 이미지 **파일**을 노트 창에 드롭하면 첨부로 복사 후 동일 삽입 — 기존 `Window_OnPreviewDrop`(`NoteWindow.xaml.cs:674`)은 지원 텍스트 파일만 열므로, 이미지 확장자 분기를 추가.

#### 저장 위치 (모드별)
| 모드 | 물리 폴더 | 노트에 기록되는 참조 |
|---|---|---|
| 볼트 | `<VaultPath>\attachments\` | `attachments/<파일명>.png` (상대 — Obsidian 호환) |
| LiteDB | `%LOCALAPPDATA%\StickyPad\attachments\` | `attachments/<파일명>.png` (동일 표기 — 렌더러가 모드별 물리 폴더로 해석) |

- 두 모드 모두 **동일한 상대 표기**를 쓴다. 절대경로를 기록하지 않는 이유: (1) 볼트 이동/백업 시 깨짐, (2) 모드 전환(LiteDB↔볼트 마이그레이션) 시 재작성 불필요, (3) HTML 재작성 시 로컬 경로 노출 표면 제거.
- 볼트 모드 안전성 확인: `VaultStore.Load` 는 `*.md` 를 `SearchOption.TopDirectoryOnly` 로만 스캔(`VaultStore.cs:42`)하므로 `attachments/` 하위폴더는 노트 로딩에 영향 없음. ✅

#### 파일명 규칙
- `paste-YYYYMMDD-HHmmss.png` (로컬 시각), 같은 초 내 충돌 시 `-2`, `-3` … suffix.
- `ExportNaming.UniqueFileName` 의 `" (2)"` 패턴은 **쓰지 않는다** — 공백·괄호가 Markdig 렌더 시 `%20` 등으로 인코딩되어 재작성·호환성 리스크가 생기므로, 공백 없는 suffix 를 쓰는 소형 헬퍼를 `AttachmentService` 안에 둔다. (제목 유래 이름이 아니므로 `SafeFileName` 정규화도 불필요.)

#### 삭제 정책 (고아 파일)
- **휴지통 이동/복원**: 첨부 파일은 건드리지 않는다 (복원 시 그대로 살아 있어야 함).
- **영구 삭제(purge)** 시:
  - **LiteDB 모드**: 삭제되는 노트의 Content 에서 `attachments/<이름>` 참조를 추출하고, **다른 모든 노트(활성+휴지통)가 참조하지 않는 파일만** 삭제한다. 베스트-에포트(실패는 로그만).
  - **볼트 모드**: **삭제하지 않는다.** 볼트는 사용자 소유 폴더로 Obsidian 등 외부 도구가 같은 attachments 를 참조할 수 있고, "사용자 파일을 뭉텅이로 지우지 않는다"는 기존 VaultStore 철학(`VaultStore.cs:18-19` 주석)과 일치시킨다. 이 비대칭은 README/CHANGELOG 에 명시.
- 참조 추출은 파일명 단위 정규식(`attachments/([\w.\-]+\.(png|jpg|jpeg|gif|webp))`, 대소문자 무시)으로 하며 마크다운/HTML 문법 모두에서 동작한다.

#### 표시 (미리보기·내보내기·WYSIWYG)
- **노트 미리보기**: 실제 이미지 렌더 (가상 호스트 매핑, §3.3.3).
- **HTML/PDF 내보내기**: data: URI 인라인으로 자기완결 문서 유지 (§3.3.5).
- **WYSIWYG**: 이미지는 `![](attachments/….png)` 소스 텍스트로 보인다(CM6 는 소스 지향 편집기 — 렌더 안 함). 표시 변경 없음 = 보안 변경 없음.

---

## 3. 기술 설계

### 3.1 신규 구성요소

| 파일 | 내용 |
|---|---|
| `stickypad/Services/IAttachmentService.cs` + `AttachmentService.cs` | 물리 attachments 폴더 해석(설정의 StorageMode/VaultPath 기반), 클립보드 이미지 → PNG 저장 후 상대 참조 반환, purge 시 고아 정리. DI 싱글턴 등록(`App.xaml.cs` 서비스 구성부). |
| `stickypad/Views/QuickCaptureWindow.xaml` + `.xaml.cs` | §2.1 의 캡처 창. 코드비하인드는 키 처리·클립보드 프리필만, 저장은 `IWindowManager` 신규 메서드에 위임. |

`AttachmentService` 핵심 시그니처(안):

```csharp
public interface IAttachmentService
{
    /// 현재 저장 모드 기준 물리 attachments 폴더(없으면 생성하지 않음 — 저장 시에만 생성).
    string GetAttachmentsDirectory();
    /// 클립보드 이미지를 PNG 로 저장하고 "attachments/<name>.png" 상대 참조를 돌려준다. 이미지 없으면 null.
    Task<string?> SaveClipboardImageAsync();
    /// 영구 삭제되는 노트들의 고아 첨부를 정리한다(LiteDB 모드 한정, 베스트-에포트).
    Task CleanupOrphansAsync(IReadOnlyList<Note> purged, IReadOnlyList<Note> remaining);
}
```

- 클립보드 읽기 순서: **① `Clipboard.GetDataObject()` 의 `"PNG"` 포맷 스트림**(알파 보존, 브라우저·캡처도구 대부분 제공) → **② `Clipboard.GetImage()` + `PngBitmapEncoder`** 폴백. DIB 경유 시 알파 손상되는 WPF 고질 이슈를 ①로 회피한다.
- 저장 직전 `Directory.CreateDirectory(attachmentsDir)`.

### 3.2 전역 빠른 캡처 — 변경 파일

| 파일 | 변경 |
|---|---|
| `stickypad/Models/AppSettings.cs` | `QuickCaptureHotkey` (기본 `"Ctrl+Shift+Space"`), `QuickCapturePrefillClipboard` (기본 `false`), `QuickCaptureOpensNote` (기본 `false`) 3개 속성 추가. |
| `stickypad/Services/IHotkeyService.cs` / `HotkeyService.cs` | `Configure` 에 세 번째 `Action quickCaptureHandler`, `Apply` 에 세 번째 gesture 인자, 상수 `QuickCaptureId = "StickyPad.QuickCapture"`, `Unregister` 에 Remove 추가. 기존 `TryRegister`(폴백·충돌 처리) 재사용(`HotkeyService.cs:46-67`). |
| `stickypad/App.xaml.cs` | 부트스트랩 배선(`App.xaml.cs:164-168`)에 세 번째 핸들러/제스처 전달. 핸들러는 `IWindowManager.ShowQuickCapture()` 호출. |
| `stickypad/Services/IWindowManager.cs` / `WindowManager.cs` | `void ShowQuickCapture()` — 단일 인스턴스 관리(기존 `_settingsWindow` 패턴). `Task<Note> CaptureNoteAsync(string text, bool showWindow)` — Note 생성(§2.1 저장 형태) → `UpsertAsync` → `BuildWindow(note)` 는 **항상 수행**하되 `showWindow` 가 false 면 `Show()` 만 생략(`IsHidden` 은 false 유지 — 다음 실행 때 정상 표시되고, `ShowAll`/트레이 토글로 즉시 드러남. `RestoreAllAsync` 의 기존 규칙 `WindowManager.cs:45-49` 과 일관). |
| `stickypad/Services/ITrayService.cs` / `TrayService.cs` | 컨텍스트 메뉴에 "빠른 캡처" 항목(기존 `MenuItem` 헬퍼), `ShowToast(string title, string message)` 추가 — `_icon?.ShowNotification(...)` 래핑, 예외는 로그 후 무시. |
| `stickypad/ViewModels/SettingsViewModel.cs` | `QuickCaptureHotkey`·`QuickCapturePrefillClipboard`·`QuickCaptureOpensNote` 바인딩 속성 + 저장 시 `hotkeys.Apply(...)` 3-인자 호출로 갱신. |
| `stickypad/Views/SettingsWindow.xaml` / `.xaml.cs` | 세 번째 핫키 캡처 박스(기존 `HotkeyBox_PreviewKeyDown` 의 `ReferenceEquals(sender, …)` 분기에 추가, `SettingsWindow.xaml.cs:43-44` 패턴) + 체크박스 2개. |
| `stickypad/Resources/Strings.cs` / `Strings.resx` / `Strings.ko.resx` | `Settings_QuickCaptureHotkey`, `Settings_QuickCapturePrefill`, `Settings_QuickCaptureOpensNote`, `Tray_QuickCapture`, `Capture_Hint`, `Capture_SavedToast` 등 en/ko 쌍. |

확인 필요 사항(구현 시 첫 단계): `HotkeyGesture.TryParse` 가 `"Space"` 키명을 파싱하는지 확인(`Utils/HotkeyGesture.cs`). WPF `Key.Space` 는 enum 이름이 `Space` 라 `Enum.TryParse` 기반이면 통과할 것. 실패하면 기본값을 `Ctrl+Shift+J` 로 교체.

보안/안정성 영향:
- 전역 훅은 기존 NHotkey 경로 그대로(신규 라이브러리 없음). 등록 실패는 이미 무해하게 처리됨.
- 클립보드 프리필은 읽기 전용·옵트인·로그 금지. 외부 전송 경로 없음.
- 캡처 창은 WebView2 를 만들지 않으므로 공격 표면 증가 없음.

### 3.3 이미지 붙여넣기 — 변경 파일과 보안 분석

#### 3.3.1 진입점: `NoteWindow.OnEditorPasting` (`Views/NoteWindow.xaml.cs:151-168`)

현재 마크업 모드에서 텍스트가 없는 붙여넣기(이미지 포함)를 `e.CancelCommand()` 로 차단한다. 이를 다음과 같이 확장:

```csharp
private void OnEditorPasting(object sender, DataObjectPastingEventArgs e)
{
    if (!IsMarkup) return;                       // 서식 모드는 기존 동작 유지

    if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
    {
        // ... 기존 평문 강제 로직 그대로 ...
        return;
    }

    e.CancelCommand();                            // 기본 붙여넣기는 항상 취소하고
    if (_viewModel.Format == NoteContentFormat.Markdown)
        _ = InsertClipboardImageAsync();          // 마크다운이면 첨부 저장 + 참조 삽입
}
```

- `InsertClipboardImageAsync`: `IAttachmentService.SaveClipboardImageAsync()` → 성공 시 `Editor.CaretPosition` 에 `![](attachments/…)` 삽입(`InsertTextInRun` + 캐럿 이동), 미리보기 디바운스 타이머 재시작. 실패(클립보드에 이미지 없음/저장 오류)는 조용히 무시(현행 차단 동작과 동일한 체감).
- `NoteWindow` 는 이미 콜백 주입 패턴(`WindowManager.cs:294`)을 쓰므로 `IAttachmentService` 도 생성자 주입 대신 `WindowManager` 가 델리게이트로 넘기거나, 창 생성부에서 서비스 인스턴스를 전달한다(기존 스타일에 맞춤).
- HTML 모드는 v1 미지원(비목표) — 이미지 붙여넣기는 계속 차단.

#### 3.3.2 표시 문제의 본질 (왜 그냥은 안 보이는가)

- 미리보기는 `NavigateToString` 기반(`NoteWindow.xaml.cs:580`) → 문서 origin 이 `about:blank` 라 `attachments/x.png` 상대경로가 **해석 자체가 안 된다**.
- 미리보기 CSP 는 `default-src 'none'; img-src data: https: http:` (`HtmlRenderer.cs:38-40`) → `file:` 스킴 이미지는 CSP 이전에 스킴 차원에서 부적합.
- WYSIWYG 가상호스트(`stickypad.editor`)는 에디터 자산 폴더만 매핑(`MarkdownWysiwyg.cs:58-59`)이고 editor.html CSP 는 `img-src data:` 뿐 — 단 WYSIWYG 는 이미지를 렌더하지 않으므로 **변경 불필요**.

#### 3.3.3 채택안: 미리보기 WebView 에 attachments 전용 가상 호스트 매핑

`NoteWindow.EnsureWebViewAsync` (`Views/NoteWindow.xaml.cs:593-635`) 에 1줄 추가:

```csharp
core.SetVirtualHostNameToFolderMapping(
    "stickypad.attachments", attachmentsDir, CoreWebView2HostResourceAccessKind.DenyCors);
```

`HtmlRenderer.Render` 는 Markdig 변환 **후** HTML 에서 `src="attachments/` 접두를 정확히 매칭해 `src="https://stickypad.attachments/` 로 재작성한다(HTML 포맷 노트의 사용자 작성 `<img>` 도 동일 치환 적용). 시그니처는 `Render(source, format, theme, bool mapAttachments = false)` 식으로 확장해 기존 호출·테스트를 깨지 않는다.

재작성 규칙(이중 방어):
- `attachments/` 로 **시작하는** 상대 src 만 재작성. `../`, 절대경로(`C:\`, `/`, `file:`), 원격 URL 은 건드리지 않는다 → 폴더 탈출 시도는 재작성 단계에서 이미 무력화.
- WebView2 의 폴더 매핑 자체도 매핑 폴더 밖 접근을 경로 정규화로 차단(2차 방어).

**대안 비교 (미리보기):**

| 방안 | 장점 | 단점 | 판정 |
|---|---|---|---|
| **가상 호스트 매핑(채택)** | CSP `img-src https:` 가 이미 허용 → CSP 무변경. NavigateToString 2MB 한계 무관. 파일 재읽기 없어 350ms 디바운스 재렌더에 부담 없음. WYSIWYG 에서 이미 검증된 메커니즘 | WebView 설정 1줄 + src 재작성 로직 필요 | ✅ |
| data: URI 인라인 | 매핑·재작성 불필요 | **NavigateToString 은 약 2MB 문자열 제한** — 스크린샷 1~2장이면 초과해 미리보기 전체가 침묵 실패. 렌더마다 파일 읽기+base64 인코딩(라이브 미리보기에 비효율) | ❌ (미리보기) |
| 미리보기를 임시 파일+가상호스트 페이지로 전환 | 상대경로 자연 해석 | NavigateToString 기반 기존 보안 검토 전체 무효화, 변경 범위 과대 | ❌ |

**보안 영향 분석 (SECURITY-REVIEW 불변식 대조):**

| 불변식 | 판정 | 근거 |
|---|---|---|
| 미리보기 스크립트 OFF | **유지** | `IsScriptEnabled=false`(`NoteWindow.xaml.cs:606`) 무변경. 매핑은 리소스 해석만 바꾸며 스크립트 실행 능력을 부여하지 않는다. 노트 HTML 이 attachments 폴더의 파일을 `<script src>` 로 참조해도 CSP `default-src 'none'`(script-src 부재) + 엔진 차원 스크립트 비활성의 이중 차단. |
| 네트워크 차단/원격 정책 불변 | **유지** | 가상 호스트는 로컬 폴더 매핑 — 실제 네트워크 요청이 아니다. CSP 문자열도 무변경(기존 `img-src https:` 로 이미 통과). 원격 이미지 허용 범위는 현행과 동일. |
| WYSIWYG 화이트리스트 | **유지** | `stickypad.editor` NavigationStarting 필터(`MarkdownWysiwyg.cs:63-67`) 무변경. WYSIWYG WebView 에는 새 매핑을 추가하지 않는다. |
| 새로 열리는 표면 | **한정적** | 악의적 노트가 `https://stickypad.attachments/<추측 이름>` 으로 폴더 내 임의 파일을 `<img>` 표시할 수 있으나, 폴더에는 본 기능이 저장한 이미지 파일만 존재하며 스크립트 OFF 라 외부 유출 채널이 없다(로컬 단일 사용자 앱 — 수용, 문서화). `DenyCors` 라 fetch/XHR 성 접근은 어차피 차단이고 스크립트 자체가 없다. |

부수 처리: 미리보기 `NavigationStarting` 핸들러(`NoteWindow.xaml.cs:615-622`)는 사용자가 `https://…` 링크 클릭 시 외부 브라우저로 여는데, `stickypad.attachments` 호스트는 브라우저에서 무의미하므로 **cancel 만 하고 열지 않는** 분기 1줄 추가.

#### 3.3.4 물리 폴더 해석

`AttachmentService.GetAttachmentsDirectory()`:
- StorageMode == "vault" && VaultPath 유효 → `Path.Combine(VaultPath, "attachments")`
- 그 외 → `Path.Combine(LOCALAPPDATA, "StickyPad", "attachments")`

`EnsureWebViewAsync` 는 창 최초 렌더 시 1회 매핑하므로, 세션 중 모드 전환(재시작 필요 — `AppSettings.cs:15` 주석)과 충돌 없음.

#### 3.3.5 내보내기(HTML/PDF): data: URI 인라인

`HtmlRenderer.RenderDocument` (`Utils/HtmlRenderer.cs:64-120`) 는 공유·인쇄용이라 CSP 가 `img-src data:` 뿐(의도적 — 추적픽셀 차단, `HtmlRenderer.cs:68-70` 주석). 여기는 **파일을 읽어 data: URI 로 인라인**한다:
- `RenderDocument(notes, title, string? attachmentsDir = null)` — 디렉터리가 주어지면 각 노트 본문 렌더 후 `src="attachments/…"` 를 base64 로 치환.
- 상한: 이미지 1장 5MB, 문서 누적 20MB. 초과·부재 파일은 원문(깨진 이미지 아이콘) 유지 — 내보내기 자체는 실패하지 않는다.
- CSP 무변경(이미 `img-src data:` 허용) → 내보낸 문서의 보안 속성 불변, 자기완결(수신자 PC 에 attachments 없어도 표시). PDF 내보내기도 동일 HTML 을 쓰므로 자동 해결.
- 호출부 `BackupService.ExportAsHtmlAsync/ExportAsPdfAsync` 에 `IAttachmentService` 폴더 전달.

#### 3.3.6 (후속) WYSIWYG 이미지 붙여넣기

v2 후보로만 기록: CM6 `paste` 이벤트에서 이미지 데이터를 base64 WebMessage(`{type:"pasteImage", data:…}`)로 호스트에 넘기고, C# 이 저장 후 `insertText` 콜백으로 참조를 삽입. `tools/mdeditor`(esbuild, `package.json` 존재 — 재빌드 가능 확인) 수정 + editor.html CSP 유지 가능. 다만 WebMessage 채널에 바이너리성 페이로드가 추가되므로 별도 보안 검토 필요.

#### 3.3.7 고아 정리·백업 연동

- `VaultRepository.PurgeAsync`/`PurgeTrashedOlderThanAsync`(`Services/VaultRepository.cs:101-113`) 및 LiteDB 대응부의 **호출자 측**(NotesList 의 영구삭제 흐름, 자동 30일 정리 지점)에서 purge 대상 노트 목록을 `IAttachmentService.CleanupOrphansAsync` 에 전달. 저장소 계층은 첨부를 모르게 유지(관심사 분리 — 저장소는 노트만, 첨부는 서비스가).
- LiteDB 모드 한정 삭제(§2.2 삭제 정책). 삭제 전 참조 카운트는 `GetAllAsync + GetTrashedAsync` 스냅샷으로 계산.
- 백업 갭(문서화): JSON 백업·볼트 ZIP 내보내기에 attachments 미포함 → CHANGELOG "알려진 제한" + 후속 이슈. 볼트 모드는 폴더째 복사하는 사용자 워크플로에서 자연히 포함된다.

### 3.4 트레이 드롭 — 기술 검토 결과: **불가, 스코프 제외 권고**

**근거:**
1. Windows 알림 영역(tray)은 explorer.exe 소유의 `Shell_TrayWnd → TrayNotifyWnd → SysPager → ToolbarWindow32` HWND 계층이다. OLE 드롭 수신(`RegisterDragDrop`)은 **해당 HWND 를 소유한 프로세스만** 등록할 수 있으므로, 서드파티 앱은 자기 트레이 아이콘 위 드롭을 받을 수 없다.
2. `Shell_NotifyIcon` API 가 앱에 전달하는 콜백 메시지는 클릭·마우스 이동 계열(NIN_*, WM_LBUTTONUP 등)뿐 — 드래그 소스 데이터가 전달되는 메시지는 존재하지 않는다.
3. H.NotifyIcon.Wpf 2.1.4(현재 사용, `stickypad.csproj:44`)의 `TaskbarIcon` 도 같은 API 를 감싸므로 드롭 관련 이벤트/속성이 아예 없다(`TrayService.cs` 의 사용 범위: Left/DoubleClick 커맨드, ContextMenu, ForceCreate — `TrayService.cs:42-58`).

**대안 비교:**

| 대안 | 평가 |
|---|---|
| A. "드롭 포켓" 미니 창 — 설정/트레이 메뉴로 켜는 화면 구석 64×64 항상-위 반투명 타깃, `AllowDrop` 로 텍스트/파일 수신 → 노트 생성 | 기술적으로 단순(기존 `Window_OnPreviewDrop` 패턴 재사용). 그러나 상시 떠 있는 신규 UI 의 유지비(모니터 구성 변화, DPI, 다른 앱 가림)와 발견성 문제 |
| B. 화면 가장자리 핫존 | 전역 마우스 훅 필요 — 보안·성능·백신 오탐 리스크로 부적합 |
| C. 기존 노트 창에 텍스트 드롭 시 새 노트 생성 | 부분 대체에 불과(노트가 하나도 안 떠 있으면 불가) |

**권고: v1 스코프 제외.** 빠른 캡처의 클립보드 프리필(복사 → 단축키 → Enter)이 "무마찰 수집" 요구의 대부분을 커버하며, 드롭 포켓(대안 A)은 사용자 수요 확인 후 후속 릴리즈에서 독립 기능으로 검토한다.

### 3.5 단일 파일 배포 영향

신규 런타임 자산 없음 — QuickCaptureWindow 는 컴파일된 XAML(BAML)이고, attachments 는 런타임 생성 폴더다. WYSIWYG 임베드 자산 규칙(임베드+추출) 변경 없음. 검증은 관례대로 **publish 산출물 기준**으로 수행한다.

---

## 4. 작업 분해 + 위험도 분류표

| # | 작업 | 파일 | 위험도 | 근거 |
|---|---|---|---|---|
| 1 | `AttachmentService` — 폴더 해석·클립보드 PNG 저장·파일명 규칙 | 신규 2파일, App.xaml.cs DI | **고위험 (Opus 직접)** | 파일 시스템 쓰기 + 클립보드 포맷 협상(PNG/DIB 폴백)의 엣지가 많음 |
| 2 | `AttachmentService.CleanupOrphansAsync` + purge 흐름 연결 | AttachmentService, purge 호출부 | **고위험 (Opus 직접)** | **사용자 파일 삭제 로직** — 참조 카운트 오류 = 데이터 손실. 모드별 비대칭 정책 포함 |
| 3 | `OnEditorPasting` 확장 + 캐럿 삽입 | NoteWindow.xaml.cs | **고위험 (Opus 직접)** | 편집기 코어 경로. RichTextBox 캐럿/포커스/undo 스택 미묘함, 기존 평문 강제 로직 회귀 금지 |
| 4 | 미리보기 가상 호스트 매핑 + `HtmlRenderer` src 재작성 + NavigationStarting 분기 | NoteWindow.xaml.cs, HtmlRenderer.cs | **고위험 (Opus 직접)** | 보안 불변식 접점(CSP·스크립트 OFF·네트워크 정책). 재작성 규칙이 방어선 |
| 5 | `RenderDocument` data: 인라인 (크기 상한 포함) | HtmlRenderer.cs, BackupService.cs | 저위험 (Sonnet 위임) | 렌더 문자열 조작 + 파일 읽기, 실패해도 내보내기 문서 품질 문제에 그침. 테스트로 커버 용이 |
| 6 | `IHotkeyService`/`HotkeyService` 3번째 단축키 + App 배선 | IHotkeyService.cs, HotkeyService.cs, App.xaml.cs | 저위험 (Sonnet 위임) | 기존 2개 패턴의 기계적 확장, 충돌 처리 기성 로직 재사용. `"Space"` 파싱 확인 선행 |
| 7 | `QuickCaptureWindow` UI + 키 처리 + 프리필 | 신규 XAML/cs | 저위험 (Sonnet 위임) | 독립 신규 창 — 기존 코드와 충돌면 없음. 승인된 UX(§2.1)를 그대로 구현. 리뷰 시 클립보드 로그 금지 확인 |
| 8 | `WindowManager.ShowQuickCapture/CaptureNoteAsync` | IWindowManager.cs, WindowManager.cs | **고위험 (Opus 직접)** | 창 수명주기(`_windows` 리스트·숨김 창) 정합성 — RestoreAll/ToggleAllVisible 회귀 위험 |
| 9 | 설정 UI(3번째 핫키 박스+체크박스 2개) + SettingsViewModel | SettingsWindow.xaml/.cs, SettingsViewModel.cs | 저위험 (Sonnet 위임) | 기존 패턴 복제(`ReferenceEquals` 분기 추가) |
| 10 | 트레이 메뉴 항목 + `ShowToast` | ITrayService.cs, TrayService.cs | 저위험 (Sonnet 위임) | 기성 MenuItem 헬퍼 + ShowNotification 래핑, 실패 무해 |
| 11 | Strings en/ko 리소스 추가 | Strings.cs, Strings.resx, Strings.ko.resx | 저위험 (Sonnet 위임) | 순수 리소스 |
| 12 | 테스트 작성(§5 단위 테스트 전체) | stickypad.Tests/Tests.cs | 저위험 (Sonnet 위임) | 테스트 코드 — 단 #2·#4 의 테스트 케이스 목록은 Opus 가 지정 |
| 13 | CHANGELOG/README/docs 갱신 + 버전 상향 | 문서, csproj, iss | 저위험 (Sonnet 위임) | 문서·버전 동기화 관례 작업 |
| 14 | (P2 선택) 이미지 파일 드롭 삽입 | NoteWindow.xaml.cs | 보류 | Window_OnPreviewDrop 확장 — #3·#4 안정화 후 결정 |

권장 순서: **#6→#8→#7→#9~11**(캡처 축) 과 **#1→#3→#4→#2→#5**(이미지 축)는 병렬 가능. #12·#13 은 각 축 완료 후.

---

## 5. 테스트·검증 계획

### 5.1 단위 테스트 (stickypad.Tests — 현재 107개에 추가)

| 대상 | 케이스 |
|---|---|
| 첨부 파일명 규칙 | `paste-\d{8}-\d{6}(-\d+)?\.png` 형식, 동일 초 충돌 시 `-2` suffix, 공백/괄호 미포함 |
| HtmlRenderer src 재작성 | `![](attachments/a.png)` → `https://stickypad.attachments/a.png`; `../attachments/`·절대경로·`file:`·원격 URL 은 **불변**; HTML 포맷 노트의 `<img src="attachments/…">` 재작성; `mapAttachments=false`(기본) 시 완전 불변(기존 테스트 회귀 없음) |
| RenderDocument 인라인 | 존재 파일 → `data:image/png;base64,…` 삽입; 부재 파일 → 원문 유지; 5MB 초과 → 미인라인; CSP 메타 문자열 불변 |
| 고아 정리 | 참조 추출 정규식(마크다운·HTML 혼합); 두 노트가 같은 파일 참조 시 한쪽 purge 로는 보존; LiteDB 모드만 삭제(볼트 모드 no-op) |
| HotkeyGesture | `"Ctrl+Shift+Space"` 파싱/포맷 왕복 |
| VaultMarkdown | 이미지 참조 포함 본문의 무손실 왕복 |
| AppSettings | 신규 3필드 기본값·직렬화 왕복(기존 설정 파일 하위호환 — 필드 부재 시 기본값) |

### 5.2 수동 검증 (publish 빌드 기준 — 단일 파일 배포 관례)

1. **캡처 흐름**: 임의 앱 포커스 상태에서 단축키 → 창 표시 → Enter 저장 → 토스트 확인 → 전체 목록에 노트 존재. `Ctrl+Enter` 로 노트 창 즉시 열림. Esc/포커스 상실 시 미저장. 단축키 재입력 시 기존 창 활성화(중복 창 없음).
2. **프리필**: 설정 ON + 텍스트 복사 → 창에 전체 선택 프리필. 클립보드에 이미지만 있을 때 빈 창.
3. **이미지 붙여넣기(LiteDB)**: 캡처도구로 화면 복사 → 마크다운 노트에 Ctrl+V → `%LOCALAPPDATA%\StickyPad\attachments\` 에 PNG 생성 + 참조 삽입 + 분할 미리보기에 이미지 표시. 텍스트+이미지 동시 클립보드 → 텍스트 우선(기존 동작).
4. **이미지 붙여넣기(볼트)**: 볼트 모드 전환 후 동일 확인 + `<볼트>\attachments\` 생성 + **Obsidian 으로 볼트를 열어 이미지 표시 확인**(상대경로 호환) + 노트 목록 로딩에 attachments 폴더 영향 없음.
5. **내보내기**: 이미지 포함 노트를 HTML/PDF 내보내기 → attachments 폴더 없는 다른 위치에서 열어도 이미지 표시(자기완결).
6. **삭제 정책**: 노트 휴지통 → 파일 보존 → 복원 → 정상 표시 → 영구 삭제(LiteDB) → 미참조 파일만 삭제, 타 노트 공유 파일 보존. 볼트 모드 영구 삭제 → 파일 보존.
7. **보안 회귀**: 노트에 `<script>alert(1)</script>` + `<img src="https://stickypad.attachments/../../settings.json">` 입력 → 스크립트 미실행·이미지 미표시 확인. 미리보기에서 원격 이미지 동작 기존과 동일.
8. **기존 107개 테스트 + 신규 테스트 전체 green**, WYSIWYG 편집/미리보기/내보내기 스모크.

---

## 6. 예상 규모와 릴리즈 제안

| 항목 | 추정 |
|---|---|
| 신규 파일 | 4개 (IAttachmentService/AttachmentService, QuickCaptureWindow.xaml/.cs) |
| 수정 파일 | 약 12개 (NoteWindow, HtmlRenderer, HotkeyService±I, WindowManager±I, TrayService±I, App, AppSettings, SettingsWindow±VM, Strings 3종, BackupService) |
| 순증 코드 | 약 700~1,000 라인 (테스트 제외) |
| 신규 테스트 | 15~20개 |
| 구현 공수 | Opus 축(고위험 5건) 1.5일 + Sonnet 축(저위험 8건) 병렬 1일 + 검증 0.5일 |

**릴리즈 버전: v2.3.0** (마이너 — 신기능 2종 추가, 데이터 스키마 변경 없음, 설정 하위호환). 트레이 드롭은 스코프 제외를 CHANGELOG 에 근거와 함께 기록하고, WYSIWYG 이미지 붙여넣기·백업 attachments 포함·드롭 포켓을 후속 후보로 남긴다.
