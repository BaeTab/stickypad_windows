# 스펙 3 — "첫인상" 조합: 앱 다크 테마 · 첫 실행 투어 · What's New

- 대상 버전: v2.2.2 → **v2.3.0 제안**
- 작성일: 2026-07-06
- 상태: 초안(구현 전 스펙). 코드 수정 없음 — 본 문서는 실제 코드(아래 인용 경로/라인) 확인 후 작성됨.

---

## 0. 현재 코드 조사 요약 (스펙의 전제)

| 항목 | 확인 결과 |
|---|---|
| WPF-UI 3.0.5 | `stickypad/stickypad.csproj:35`에 참조. `stickypad/App.xaml:9-10`에서 `<ui:ThemesDictionary Theme="Light" />` + `<ui:ControlsDictionary />`가 **이미 머지되어 사용 중**. 단 테마가 Light로 하드코딩이고, `ui:` 네임스페이스 컨트롤·`Wpf.Ui` C# API는 어디에도 미사용 (전체 grep 결과 App.xaml 3줄이 전부). 즉 표준 컨트롤(Button/TextBox/CheckBox/ComboBox/ContextMenu 등)의 암시적 스타일은 이미 WPF-UI 것을 쓰고 있으므로, **ThemesDictionary를 Dark로 스왑하면 컨트롤류는 자동으로 다크가 된다.** 남는 문제는 창/텍스트/카드에 박힌 하드코딩 hex뿐. |
| 노트 팔레트 | `stickypad/Models/NoteTheme.cs` — `NotePalette`에 6색(`Yellow/Pink/Blue/Green/Purple/Gray`). **Gray는 이미 다크톤**(배경 `#373A40`, 전경 `#ECEEF2`). 별도의 "다크 팔레트 변형" 항목은 없음. |
| 노트 창 크롬 | `NoteWindow.xaml`은 앱 크롬 색을 쓰지 않고 `NoteViewModel`의 `BackgroundBrush/HeaderBrush/…`(= `NotePalette.For(Color)`, `ViewModels/NoteViewModel.cs:83-88`)에 바인딩 — **앱 테마와 독립적**. 예외: 색상 피커 Popup 배경 `#FFFFFF`(`NoteWindow.xaml:166`). |
| 하드코딩 색 | `NotesListWindow.xaml`(`#FAFAFA`, `White`, `#EFEFEF`, `#1976D2`, `#666/#888/#999`, `#E5E5E5`, `#EAF3FF`, `#FBE9E7`, `#E3F2FD`, `#B85C5C`), `SettingsWindow.xaml`(`#FAFAFA`, `#888`, `#C62828`). §3.2에 전체 목록. |
| 설정 패턴 | `Models/AppSettings.cs` — 평면 POCO + `System.Text.Json`. `Language`("system"/"en"/"ko")·`StorageMode` 스트링 옵션 선례. `SettingsService.Load()`(`Services/SettingsService.cs:28-41`)는 파일 부재 시 기본값 — **파일 존재 여부가 밖으로 노출되지 않음**(첫 실행 감지에 필요, §3.5). UI는 `SettingsViewModel`의 `LanguageOption` record + ComboBox 패턴(`ViewModels/SettingsViewModel.cs:19, 52-57`). |
| 시작 흐름 | `App.xaml.cs:128-175` — `ApplyCulture` → 휴지통 purge → `WindowManager.RestoreAllAsync()` → 트레이 init → InstanceChannel → 파일 인자 열기 → 핫키 → 업데이트 체크. `RestoreAllAsync`(`Services/WindowManager.cs:35-50`)는 **노트 0개면 빈 노트 1장 자동 생성**. 빈 자리표시 노트는 `DiscardEmptyPlaceholders`(`WindowManager.cs:264-271`)가 정리. |
| 버전 획득 | `Services/UpdateService.cs:198-199` `CurrentVersion()`(Assembly 버전, private) / `Compare()`(internal, Major.Minor.Build 비교, 테스트 존재). csproj `<Version>2.2.2`. |
| WebView2 표면 | ① 미리보기 `Preview`(스크립트 OFF) — `Utils/HtmlRenderer.Render(source, format, NoteTheme)`가 노트 테마 색으로 래핑(`HtmlRenderer.cs:30-57`). 오버레이가 `rgba(0,0,0,…)` 고정이라 Gray(다크) 노트에서 코드블록·표 경계 대비가 낮음. ② WYSIWYG `WysiwygEditor` — `Utils/MarkdownWysiwyg.cs`가 `editor.html?lang=xx`로 내비게이트(89행 부근, 쿼리 파라미터 선례). `Assets/mdeditor/editor.html` 툴바가 라이트 고정(`rgba(255,255,255,.55)`, 글자 `#2a2a2a`), `tools/mdeditor/editor.js`의 CM6 테마는 `color: inherit`/`currentColor`라 본문 글자색은 호스트 CSS로 제어 가능. 에디터 자산은 EmbeddedResource로 임베드 후 런타임 추출(단일 파일 배포 대응, csproj:23-30 주석). |
| i18n | `Resources/Strings.resx` + `Strings.ko.resx`, 수동 접근자 `Resources/Strings.cs`(`Get(nameof(...))` 패턴). |
| CHANGELOG | 루트 `CHANGELOG.md`, Keep-a-Changelog 형식, `## [X.Y.Z] - 날짜` 헤더. 한국어 위주. **빌드 산출물에 포함되지 않음**(단일 파일 exe — What's New에 쓰려면 임베드 필요). |
| 테스트 | `stickypad.Tests/Tests.cs` 단일 파일, `[Fact]`/`[Theory]` 72개(케이스 107개). `InternalsVisibleTo` 설정됨(csproj:20). |

---

## 1. 목표 / 비목표

### 목표
1. **앱 크롬 다크 테마** — 노트 목록·설정·(신규) What's New 창, 트레이/노트 컨텍스트 메뉴 등 "앱 크롬"이 시스템 따라가기/라이트/다크 3옵션을 지원. 저장 즉시 적용(재시작 불필요).
2. **첫 실행 투어** — 최초 실행 시 빈 노트 1장 대신 **핵심 기능을 설명하는 샘플 노트 3장**을 생성해 제품 자체(스티키 노트)로 온보딩. 삭제로 스킵, 설정에서 재생성 가능.
3. **What's New** — 업데이트 후 첫 실행에 새 버전의 변경점을 1회 표시(CHANGELOG.md 임베드·재활용). 첫 설치에는 표시하지 않음.
4. 세 기능이 공유하는 기반: `AppSettings` 확장(`AppTheme`/`LastSeenVersion`/`OnboardingCompleted`) + 시작 흐름 게이트.

### 비목표
- **노트 자체 색상 팔레트 변경 없음.** 노트 창은 지금처럼 자기 팔레트(6색)를 따른다. 다크 팔레트 "변형" 검토 결과는 §3.4 — 이번 릴리즈에서는 **도입하지 않기로 결정**(근거 포함), 설계만 남긴다.
- 내보내기 HTML/PDF/인쇄 문서(`HtmlRenderer.RenderDocument`)는 인쇄·공유물이므로 **항상 라이트 유지**.
- Win32 `MessageBox`·파일/폴더 대화상자·시스템 트레이 아이콘의 다크 변형(OS 소관, 알려진 한계로 문서화만).
- 인터랙티브 오버레이형 투어(코치마크)·애니메이션 — 멀티 윈도우 트레이 앱에 부적합, 샘플 노트 방식으로 갈음.
- 업데이트 알림 강화·자동 릴리즈 노트 다국어화.

---

## 2. UX 정의

### 2.1 테마 설정
- 설정 창에 **"테마"** ComboBox 추가(언어 콤보 바로 아래, 동일 레이아웃): `시스템 설정 따르기`(기본) / `라이트` / `다크`.
- **적용 시점: [저장] 클릭 즉시.** 재시작 불필요(기존 `Settings_RestartNote` 문구는 언어·저장소에만 해당하므로 그대로 둠). 열려 있는 모든 앱 크롬 창이 즉시 갱신된다(DynamicResource).
- `시스템 설정 따르기` 선택 시, 실행 중 OS 라이트/다크 전환(개인 설정 변경)도 실시간 반영.
- 노트 창은 어떤 테마에서도 **자기 노트 색을 유지**한다(다크에서 어두운 노트를 원하는 사용자는 기존 Gray 색을 쓰면 됨 — 안내 노트에 한 줄 언급).
- 다크에서 표준 타이틀바가 흰색으로 남지 않도록 DWM 다크 타이틀바 적용(§3.1.4).

### 2.2 첫 실행 투어(샘플 노트)
- **노출 조건(전부 충족)**: ① `settings.json`이 존재하지 않던 최초 실행, ② 저장소의 노트가 0개. → 기존 "빈 노트 1장 자동 생성" 대신 샘플 노트 3장을 시드하고 그대로 화면에 띄운다(계단식 배치).
- 기존 사용자가 업그레이드한 경우(설정 파일 존재)에는 **절대 발동하지 않는다.** 노트를 전부 지운 기존 사용자도 기존 동작(빈 노트 1장) 유지.
- 샘플 노트는 **평범한 마크다운 노트**다: 태그·체크박스가 실제로 동작하고, 목록 창에 나타나며, 여느 노트처럼 삭제(=스킵)할 수 있다. 첫 장 상단에 "다 읽으면 지우세요" 명시.
- **재보기**: 설정 창 하단에 링크형 버튼 "시작 안내 노트 다시 만들기" — 누르면 샘플 3장을 새로 생성(기존 노트와 무관하게 추가). 저장 버튼과 독립 동작.
- 샘플 노트 구성(내용은 리소스로 en/ko 지역화, `{{date}}` 치환은 불필요):

| # | 색 | 제목(H1) | 내용 요지 |
|---|---|---|---|
| 1 | Yellow | StickyPad에 오신 것을 환영합니다 | 헤더 드래그 이동, 왼쪽 색 점=색 변경, Pin=항상 위, x=숨기기(저장됨), 트레이 아이콘 좌클릭=모두 표시/숨김. "이 안내 노트 3장은 읽고 나면 Del로 지우세요." |
| 2 | Blue | 마크다운과 편집 모드 | 헤더의 T/M/</> 모드 토글, ✎=위지윅 편집, Eye=미리보기. 체크박스 `- [ ]` 예시, `#태그` 예시(실제 태그로 인덱싱되어 목록 필터에 등장). |
| 3 | Green | 모든 노트·볼트·백업 | `Ctrl+Shift+L` 목록 창(검색·태그 필터·내보내기), `Ctrl+Shift+N` 새 노트, 트레이 메뉴(백업·볼트 모드 `.md` 폴더 저장), 설정에서 다크 테마 전환 한 줄. |

### 2.3 What's New
- **노출 규칙(전부 충족 시 1회)**: ① 최초 설치 실행이 아님(§2.2와 상호배타 — 첫 실행이면 `LastSeenVersion`만 현재로 기록하고 표시 생략), ② 저장된 `LastSeenVersion`이 없거나 현재 버전보다 낮음(`UpdateService.Compare` 재사용, 다운그레이드면 표시 없이 기록만 갱신), ③ 이번 실행이 **파일 인자 실행이 아님**(`.md` 더블클릭으로 켜진 순간에 끼어들지 않는다 — 이 경우 다음 일반 실행으로 미룸).
- 표시 시점: `RestoreAllAsync`·트레이 초기화가 끝난 뒤(시작 실패 경로와 분리, 실패해도 앱 시작에 영향 없도록 try/catch + 로그).
- **창이 뜨는 즉시** `LastSeenVersion = 현재 버전`으로 저장 — 사용자가 읽지 않고 꺼도 재표시하지 않는다(성가심 방지 우선).
- 내용: 임베드된 CHANGELOG.md에서 **현재 버전 섹션만** 추출해 렌더(§3.6). 섹션을 못 찾으면 창 자체를 띄우지 않는다(silent skip + 로그). 창 하단 "닫기" 버튼 1개, `ShowInTaskbar=false`, 크기 460×540 고정, 앱 테마를 따름.
- 언어: CHANGELOG가 한국어 위주이므로 영어 UI 사용자에게도 한국어 본문이 보일 수 있음 — **수용하는 한계**로 문서화(창 제목·버튼만 지역화).

---

## 3. 기술 설계

### 3.1 테마 인프라 (핵심 결정)

#### 3.1.1 결정: 자체 테마 시스템 대신 **WPF-UI를 활용**한다
비교:

| | A. WPF-UI `ApplicationThemeManager` 활용 (채택) | B. 자체 ResourceDictionary 테마 시스템 |
|---|---|---|
| 컨트롤 스타일 | 이미 `ControlsDictionary`가 표준 컨트롤을 재스타일 중 → `ThemesDictionary` 스왑만으로 Button/TextBox/CheckBox/ComboBox/ContextMenu/ScrollBar/Menu 전부 다크 전환 | 모든 컨트롤 다크 스타일을 직접 작성(수백 라인)하거나 WPF-UI와 충돌 |
| 추가 의존성 | 없음(3.0.5 참조·머지 완료) | 없음이지만 사실상 WPF-UI 제거 작업이 선행돼야 일관성 확보 |
| 유지보수 | WinUI 토큰 키를 따름(문서화된 공개 리소스) | 키 체계·색 값 전부 자체 부담 |
| 리스크 | 테마 스왑 시 전역 시각 회귀(모든 창 검증 필요) | 동일 + 작성량 큼 |

→ **A 채택.** 단, WPF-UI 토큰만으로 부족한 앱 고유 색(브랜드 액센트 `#1976D2`, 휴지통 적색 계열 등)은 얇은 자체 사전 1쌍으로 보완한다(§3.1.3).

#### 3.1.2 `ThemeService` (신규 `Services/ThemeService.cs` + `IThemeService.cs`)
```csharp
public enum AppThemeKind { Light, Dark }

public interface IThemeService
{
    AppThemeKind Effective { get; }          // 시스템 따라가기 해소 후의 실제 테마
    void Apply(string? setting);             // "system" | "light" | "dark"
    event EventHandler<AppThemeKind>? Changed;
}
```
- `Apply` 동작: ① `"system"`이면 레지스트리 `HKCU\...\Themes\Personalize\AppsUseLightTheme` 판독으로 해소, ② `Wpf.Ui.Appearance.ApplicationThemeManager.Apply(ApplicationTheme.Light|Dark, WindowBackdropType.None, updateAccent: false)` — App.xaml에 머지된 `ThemesDictionary`를 런타임 스왑, ③ 자체 보조 사전(`AppColors.Light/Dark.xaml`) 스왑, ④ 열린 모든 `Window`에 DWM 다크 타이틀바 재적용(§3.1.4), ⑤ `Changed` 발화.
- `"system"`일 때 `Microsoft.Win32.SystemEvents.UserPreferenceChanged`(General 카테고리) 구독으로 OS 전환 실시간 반영. `"light"/"dark"`로 바뀌면 구독 해제. (`SystemEvents`는 정적 이벤트 — 앱 종료 시 해제. `Microsoft.Win32.Registry` 패키지는 이미 참조: csproj:46)
- **주의(구현 시 검증 항목)**: WPF-UI 3.0.5의 `ApplicationThemeManager.Apply` 오버로드 시그니처와, 표준 컨트롤 암시적 스타일이 실제로 테마 브러시를 DynamicResource로 참조하는지 데모로 선검증할 것(고위험 작업 1번의 첫 단계).
- DI: `services.AddSingleton<IThemeService, ThemeService>()` (`App.xaml.cs:92-123` 블록). 호출 시점: `App.OnStartup`에서 `LocalizationService.ApplyCulture` 직후·`RestoreAllAsync` 이전(`App.xaml.cs:130` 부근) — 창이 하나라도 뜨기 전에 적용.

#### 3.1.3 리소스 계층
- **1층 — WPF-UI 토큰(그대로 사용, DynamicResource)**: `ApplicationBackgroundBrush`(창 배경), `TextFillColorPrimaryBrush`/`SecondaryBrush`/`TertiaryBrush`(본문/보조/희미한 텍스트), `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush`(노트 카드), `ControlFillColorSecondaryBrush`(칩/호버). 구현 첫 단계에서 실키 존재를 Wpf.Ui 소스(Themes/Light.xaml·Dark.xaml)로 확인하고, 없으면 2층으로 내린다.
- **2층 — 자체 보조 사전(신규 `Themes/AppColors.Light.xaml`·`Themes/AppColors.Dark.xaml`)**: App.xaml MergedDictionaries에 추가하고 ThemeService가 스왑. 키(전부 `SolidColorBrush`):

| 키 | Light(현재 값 유지) | Dark(제안) | 용도 |
|---|---|---|---|
| `App.AccentBrush` | `#1976D2` | `#4FA3E3` | 탭 선택·태그 텍스트·선택 테두리 |
| `App.AccentSubtleBrush` | `#EAF3FF` | `#1E3A5F` | 선택 카드 배경 |
| `App.InfoSubtleBrush` | `#E3F2FD` | `#1C3550` | 복원 버튼 배경 |
| `App.DangerSubtleBrush` | `#FBE9E7` | `#4A2320` | 휴지통 비우기/영구삭제 버튼 배경 |
| `App.DangerTextBrush` | `#C62828` | `#EF9A9A` | 검증 오류·삭제일 텍스트 |
| `App.ChipBrush` | `#EFEFEF` | `#3A3D42` | 태그 칩·탭 호버 배경 |
| `App.PopupBackgroundBrush` | `#FFFFFF` | `#2B2D31` | NoteWindow 색상 피커 Popup |

- 다크 색 값은 구현 시 스크린샷 하네스(§5)로 대비(WCAG AA, 본문 4.5:1) 확인 후 미세 조정 가능 — 스펙의 값은 출발점.

#### 3.1.4 다크 타이틀바 (신규 `Utils/WindowThemeHelper.cs`)
- `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE = 20, ref BOOL, 4)` P/Invoke. 실패는 무시(best-effort, Win10 1809 이전 폴백 불필요 — 20 미지원이면 라이트 타이틀바로 남을 뿐).
- 적용 지점: 각 크롬 창(`NotesListWindow`/`SettingsWindow`/`WhatsNewWindow`)의 `SourceInitialized`에서 1회 + `IThemeService.Changed` 시 `Application.Current.Windows` 순회 재적용. `NoteWindow`는 `WindowStyle=None`이라 대상 아님.

### 3.2 하드코딩 색 마이그레이션 목록 (기계적 치환 — 저위험 위임 대상)

`NotesListWindow.xaml`:

| 위치(라인) | 현재 | 치환 |
|---|---|---|
| 12 `Window Background` | `#FAFAFA` | `{DynamicResource ApplicationBackgroundBrush}` |
| 37 TabToggle 호버 | `#EFEFEF` | `{DynamicResource App.ChipBrush}` |
| 40-41 TabToggle 선택 | `#1976D2`/`White` | `{DynamicResource App.AccentBrush}` / 유지(White) |
| 69, 103 카운트 텍스트 | `#666` | `{DynamicResource TextFillColorSecondaryBrush}` |
| 151 휴지통 수 | `#999` | `{DynamicResource TextFillColorTertiaryBrush}` |
| 157 휴지통 비우기 버튼 | `#FBE9E7` | `{DynamicResource App.DangerSubtleBrush}` |
| 189 태그 칩 배경 | `#EFEFEF` | `{DynamicResource App.ChipBrush}` |
| 196 칩 카운트 | `#888` | `{DynamicResource TextFillColorTertiaryBrush}` |
| 224-225 카드 배경/테두리 | `White`/`#E5E5E5` | `{DynamicResource CardBackgroundFillColorDefaultBrush}` / `{DynamicResource CardStrokeColorDefaultBrush}` |
| 229-230 선택 카드 | `#EAF3FF`/`#1976D2` | `{DynamicResource App.AccentSubtleBrush}` / `{DynamicResource App.AccentBrush}` |
| 268 발췌 텍스트 | `#666` | `{DynamicResource TextFillColorSecondaryBrush}` |
| 276 태그 라인 | `#1976D2` | `{DynamicResource App.AccentBrush}` |
| 287 수정일 | `#999` | `{DynamicResource TextFillColorTertiaryBrush}` |
| 293 삭제일 | `#B85C5C` | `{DynamicResource App.DangerTextBrush}` |
| 311 복원 버튼 | `#E3F2FD` | `{DynamicResource App.InfoSubtleBrush}` |
| 320 영구삭제 버튼 | `#FBE9E7` | `{DynamicResource App.DangerSubtleBrush}` |
| 339, 353, 358 빈 목록/푸터 | `#888` | `{DynamicResource TextFillColorTertiaryBrush}` |

`SettingsWindow.xaml`:

| 위치(라인) | 현재 | 치환 |
|---|---|---|
| 9 `Window Background` | `#FAFAFA` | `{DynamicResource ApplicationBackgroundBrush}` |
| 94, 155 힌트 텍스트 | `#888` | `{DynamicResource TextFillColorTertiaryBrush}` |
| 164 검증 오류 | `#C62828` | `{DynamicResource App.DangerTextBrush}` |

`NoteWindow.xaml` (노트 크롬은 노트 팔레트 유지 — 앱 테마 대상 **아님**, 아래만 예외):

| 위치(라인) | 현재 | 치환 |
|---|---|---|
| 166-168 색상 피커 Popup | `#FFFFFF`/`#33000000` | `{DynamicResource App.PopupBackgroundBrush}` / `{DynamicResource CardStrokeColorDefaultBrush}` |
| 그 외 `#11000000`~`#33000000` 오버레이 | 유지 | 노트 색 위 상대 오버레이 — 변경 금지(Gray 노트 대비 이슈는 §3.4 후속으로) |

`HighlightedText`(`Utils/HighlightedText.cs`)의 검색 하이라이트 색이 코드에 있다면 함께 점검(구현 시 확인 — Foreground 상속이면 무변경).

### 3.3 WebView2 표면의 다크 대응

원칙: **미리보기·위지윅은 앱 테마가 아니라 "노트 테마"를 따른다**(노트 배경 위에 그려지므로). 따라서 "다크 대응"의 정확한 의미는 *어두운 노트(현 Gray, 향후 다크 변형)에서의 가독성 보장*이다.

1. **`HtmlRenderer.Wrap`(`Utils/HtmlRenderer.cs:30-57`)** — 시그니처 유지(`NoteTheme` 이미 수신). 내부에서 배경 휘도로 다크 판정을 추가한다:
   ```csharp
   // 상대 휘도(sRGB 근사) < 0.4 → 다크 노트로 간주
   private static bool IsDark(Color bg) => (0.2126*bg.R + 0.7152*bg.G + 0.0722*bg.B) / 255.0 < 0.4;
   ```
   다크 판정 시: `code`/`pre` 배경 `rgba(0,0,0,.06)` → `rgba(255,255,255,.08)`, 표 경계·`hr` `rgba(0,0,0,…)` → `rgba(255,255,255,…)`, 링크 `#1565C0` → `#6FB1E8`. 라이트 판정 시 현재 값 그대로 → **기존 5색 라이트 노트는 렌더 결과가 바이트 단위로 불변**(회귀 없음).
2. **`editor.html`** — `MarkdownWysiwyg.LoadAsync`의 내비게이트 URL(현행 `?lang=xx`, `Utils/MarkdownWysiwyg.cs:80`)에 `&theme=dark|light`를 추가(판정은 위와 동일한 휘도 함수 — `NoteViewModel.Theme.Background`를 `NoteWindow.xaml.cs`에서 전달). `editor.html`의 인라인 CSS에 `:root[data-theme="dark"]` 변형 추가: 툴바 배경 `rgba(255,255,255,.55)` → `rgba(0,0,0,.35)`, 버튼 글자 `#2a2a2a` → `#e6e6e6`, 구분선·호버 반전. 부트 스크립트(`tools/mdeditor/editor.js`)가 쿼리스트링에서 `theme`을 읽어 `document.documentElement.dataset.theme` 지정(기존 `lang` 파싱 코드 옆). CM6 텍스트는 `color: inherit`(editor.js:80)이므로 `body{color:…}`만 테마별로 주면 됨. 링크 색(editor.js:20-21 `#2563eb`)은 다크에서 `#7cb0f5`로 하이라이트 스타일 분기. **번들 재빌드 필요**(`tools/mdeditor/build.mjs`) — CI의 번들-소스 일치 검증(CHANGELOG 2.2.1 항목)에 걸리지 않게 lockfile 그대로 재빌드.
3. 보조: `CoreWebView2.Profile.PreferredColorScheme`을 노트 테마에 맞춰 지정(스크롤바 등 브라우저 기본 UI 다크화, best-effort — try/catch).
4. `RenderDocument`(내보내기)는 무변경(비목표).

### 3.4 노트 다크 팔레트 변형 — 검토 결과: **이번 릴리즈 제외**

- 근거: ① 스티키 노트의 정체성이 "색종이"라 다크 배경 노트는 사용자 선호가 갈림 — 이미 Gray가 다크 노트 수요를 커버, ② `NoteViewModel.Theme => NotePalette.For(Color)`(`NoteViewModel.cs:83`)가 정적 조회라 앱 테마 의존성을 넣으면 노트 6색 × 앱 2테마 매트릭스와 `HtmlRenderer`/WYSIWYG/내보내기 전 표면의 회귀 검증이 필요 — 본 조합의 다른 두 기능과 함께 넣기엔 위험 과다, ③ 볼트/DB에는 `NoteColor` enum만 저장되므로 나중에 넣어도 데이터 마이그레이션이 전혀 없음(추가가 쉬운 구조).
- 후속 설계 메모(차기): `NotePalette.For(NoteColor, AppThemeKind)` 오버로드 + 다크 변형 테이블(예: Yellow → 배경 `#4A4322`/전경 `#F2ECCB`), `NoteViewModel`이 `IThemeService.Changed` 구독 후 브러시 5종 재통지. §3.3의 휘도 판정 덕에 렌더 계층은 수정 없이 자동 대응된다.

### 3.5 첫 실행 투어

#### 설정·감지
- `AppSettings`(패턴: `Models/AppSettings.cs`)에 추가:
  ```csharp
  /// 앱 크롬 테마: "system"(OS 따라), "light", "dark".
  public string AppTheme { get; set; } = "system";
  /// What's New를 마지막으로 표시한(또는 표시를 생략한) 앱 버전. null=기록 없음.
  public string? LastSeenVersion { get; set; }
  /// 첫 실행 샘플 노트를 시드했는지(재시드 방지).
  public bool OnboardingCompleted { get; set; }
  ```
  기존 settings.json에 키가 없으면 System.Text.Json이 기본값으로 채움 — 마이그레이션 불요.
- `ISettingsService`/`SettingsService`에 `bool LoadedFromFile { get; }` 추가 — `Load()`에서 `File.Exists(_path)` 결과 보존(`SettingsService.cs:32`). **첫 설치 판정 = `!LoadedFromFile`** (업그레이드 사용자는 `OnboardingCompleted=false`여도 파일이 존재하므로 투어가 발동하지 않는다 — 이 게이트가 핵심).

#### `OnboardingService` (신규 `Services/OnboardingService.cs` + 인터페이스)
```csharp
public interface IOnboardingService
{
    /// 샘플 노트 3장을 저장소에 시드. force=false면 첫 설치+노트 0개일 때만.
    Task<bool> SeedAsync(bool force = false);
}
```
- 의존: `INoteRepository`, `ISettingsService`. 내용은 `Strings`에서 조회(키: `Onboarding_Note1Title/Body` … `Note3…` — 본문은 마크다운 원문 통짜 문자열, `NoteContentFormat.Markdown`).
- 노트 생성은 `WindowManager.CreateAndShowNew(NoteTemplate)`(`WindowManager.cs:77-103`)의 필드 채움 방식을 따른다: `PlainText = TextExtraction.ToPlainText(...)`, `Title = DeriveTitle(...)`, `Tags = ExtractTags(...)` — 태그·검색 인덱싱이 실노트와 동일하게 동작해야 투어 내용(태그 필터 데모)이 성립.
- 배치: `X/Y = 120+i*40, 140+i*40`, `Width/Height = 320×380`(온스크린 보정은 `BuildWindow`의 `MonitorHelper.EnsureOnScreen`이 담당 — 추가 작업 없음).
- 시드 후 `OnboardingCompleted = true` + `SaveAsync()`.

#### `RestoreAllAsync`와의 관계 (질문받은 정리)
- `App.OnStartup`에서 **`RestoreAllAsync()` 호출 직전에** 시드한다(`App.xaml.cs:145-146` 사이):
  ```csharp
  var isFirstRun = !settingsService.LoadedFromFile;
  if (isFirstRun && !settings.OnboardingCompleted)
      await onboarding.SeedAsync().ConfigureAwait(true);
  await manager.RestoreAllAsync().ConfigureAwait(true);
  ```
- 이렇게 하면 `RestoreAllAsync`의 "0개면 빈 노트 1장"(`WindowManager.cs:39-43`) 분기는 **수정하지 않는다** — 시드가 성공하면 노트 3개가 있으므로 그 분기에 진입하지 않고, 시드가 실패(예: 저장소 오류)하면 기존 빈 노트 1장 폴백이 그대로 안전망이 된다. 샘플 노트는 내용이 있으므로 `DiscardEmptyPlaceholders`(`WindowManager.cs:264-271`)·빈 노트 즉시 purge(`OnWindowDismissed`)의 영향도 받지 않는다.
- 재보기(설정 버튼)는 `SeedAsync(force: true)` 후 `WindowManager.ReloadAsync()`가 아니라 **새 노트만 창으로 띄우는** 경로가 필요 — `IWindowManager`에 `Task ShowNotesAsync(IReadOnlyList<Note>)` 소형 메서드 추가(내부 `BuildWindow`+`Show` 재사용) 또는 SeedAsync가 Note 목록을 반환하고 SettingsViewModel이 창 표시를 위임. 전자를 권장.

### 3.6 What's New

#### 데이터: CHANGELOG.md 임베드 (하드코딩 요약 대신 채택)
- 근거: 릴리즈마다 이중 관리(요약 하드코딩)를 없애고 단일 소스 유지. 메모리에 기록된 단일 파일 배포 원칙("Content 자산은 exe에 실리지 않음 — 임베드하라")에 따라 **EmbeddedResource**로 임베드한다. csproj의 기존 mdeditor 임베드 블록(csproj:23-30) 옆에 추가:
  ```xml
  <EmbeddedResource Include="..\CHANGELOG.md" Link="Assets\CHANGELOG.md" />
  ```
- 파서(신규 `Utils/ChangelogReader.cs`, `internal static` — 테스트 용이):
  ```csharp
  /// CHANGELOG 마크다운에서 "## [X.Y.Z]" 섹션(해당 헤더부터 다음 "## [" 직전까지)을 추출. 없으면 null.
  internal static string? ExtractSection(string changelogMarkdown, Version version);
  ```
  헤더 매칭은 `^## \[v?{Major}.{Minor}.{Build}\]` 정규식(대괄호 이스케이프, 앵커드) — `UpdateService.IsSafeReleaseTag`와 같은 보수적 스타일.
- 버전 획득: `UpdateService.CurrentVersion()`(`UpdateService.cs:198-199`)은 private — 로직이 한 줄이므로 신규 `Utils/AppVersion.cs`(`internal static Version Current()`)로 승격하고 UpdateService도 이를 쓰도록 정리(동작 불변). 버전 비교는 기존 `UpdateService.Compare` 재사용.

#### 창: `Views/WhatsNewWindow.xaml(.cs)` (신규)
- 구성: 제목("새로워진 점 — v2.3.0", 지역화 포맷 문자열) + **스크립트 OFF WebView2**(미리보기 `Preview`와 동일 보안 프로파일: `NavigateToString`, 스크립트/웹메시지 비활성 기본값 유지) + 닫기 버튼. 앱 크롬 색은 §3.1 리소스 사용.
- 렌더: `HtmlRenderer`에 앱 크롬용 소형 래퍼 추가 —
  ```csharp
  /// What's New 등 앱 크롬 문서용: 마크다운을 앱 테마(라이트/다크) 색으로 래핑해 렌더.
  public static string RenderAppDocument(string markdown, bool dark);
  ```
  내부는 기존 `Render`/`Wrap`의 CSP(‘default-src none; style-src unsafe-inline …’)를 그대로 복제하고 배경/전경만 앱 테마 색(`#FAFAFA/#1F2328` vs `#202124/#E8EAED`)으로 치환. WebView2 초기화 실패(런타임 부재 등) 시 창을 띄우지 않고 로그만 남긴다(silent).
- 트리거(`App.OnStartup`, 트레이 init 뒤 — `App.xaml.cs:148` 이후):
  ```csharp
  var current = AppVersion.Current();
  var last = Version.TryParse(settings.LastSeenVersion, out var v) ? v : null;
  var isUpgrade = !isFirstRun && (last is null || UpdateService.Compare(current, last) > 0);
  if (settings.LastSeenVersion != current.ToString(3))
  { settings.LastSeenVersion = current.ToString(3); _ = settingsService.SaveAsync(); }
  if (isUpgrade && startupFiles.Length == 0) /* WhatsNewWindow 표시 (try/catch) */;
  ```
  (다운그레이드·첫 설치는 기록만 갱신하고 표시 생략 — §2.3 규칙과 1:1 대응)

### 3.7 설정 UI·i18n·DI 변경 요약
- `SettingsViewModel`: `ThemeOption(string Value, string Label)` record + `ThemeOptions`(system/light/dark) + `[ObservableProperty] string _selectedAppTheme` — `LanguageOption` 패턴(`SettingsViewModel.cs:19, 52-57`) 복제. `SaveAsync`에서 `current.AppTheme = SelectedAppTheme;` 저장 후 `_theme.Apply(SelectedAppTheme)` 호출(생성자 주입 `IThemeService` 추가). "시작 안내 노트 다시 만들기"용 `[RelayCommand] ReseedOnboardingAsync`.
- `SettingsWindow.xaml`: 언어 콤보(Row 7) 아래에 테마 Row 삽입(Grid RowDefinition 1개 추가), 하단에 링크형 버튼 1개.
- 신규 리소스 키(en/ko 각 1벌, `Strings.cs` 접근자 동반): `Settings_Theme`, `Settings_ThemeSystem/Light/Dark`, `Settings_ReseedOnboarding`, `Onboarding_Note1Title/Body`~`Note3Title/Body`, `WhatsNew_WindowTitle`, `WhatsNew_TitleFormat`, `Common_Close` — 약 15키.
- DI 등록 2건(`IThemeService`, `IOnboardingService`) — `App.xaml.cs:113-122` 블록.

---

## 4. 작업 분해 + 위험도 분류

> 원칙: **테마 인프라·시작 흐름·데이터 시드 = 고위험(Opus 직접)** — 전역 회귀/사용자 데이터에 닿는 지점. **패턴 복제·기계적 치환·콘텐츠 = 저위험(Sonnet 위임)** — 선례가 파일 안에 있고 실패가 국소적.

| # | 작업 | 위험도 | 담당 | 근거 |
|---|---|---|---|---|
| T1 | `ThemeService` + App.xaml `ThemesDictionary` 런타임 스왑 + `AppColors.Light/Dark.xaml` + 시스템 감지(SystemEvents) + DWM 타이틀바 헬퍼. WPF-UI 3.0.5 API 실검증 포함 | **고** | Opus | 전 창에 영향, WPF-UI 내부 동작 검증 필요, 정적 이벤트 수명 관리(누수 위험) |
| T2 | 시작 흐름 게이트: `AppSettings` 3필드, `LoadedFromFile`, 첫 실행/업그레이드 판정, What's New 트리거, 시드 호출 순서(`App.xaml.cs`) | **고** | Opus | 오판 시 기존 사용자에게 투어 오발동·샘플 노트가 실DB에 잘못 시드 — 사용자 데이터 오염 경로 |
| T3 | `OnboardingService` 시드 로직(+`IWindowManager.ShowNotesAsync`) | **고** | Opus | 저장소 쓰기 + `RestoreAllAsync` 경합/폴백 상호작용 |
| T4 | `ChangelogReader` 파서 + CHANGELOG 임베드 + `AppVersion` 승격 + **단일 파일 publish 빌드에서 임베드 검증** | **고** | Opus | 단일 파일 자산 사고 전례(메모리·CHANGELOG 2.1.1) — 반드시 publish 산출물로 확인 |
| T5 | `HtmlRenderer` 휘도 판정 + 다크 노트 CSS 분기, `RenderAppDocument` | 중→**고** 취급 | Opus | 렌더 회귀가 모든 마크다운 노트에 파급(라이트 5색 불변 조건 검증 필수) |
| T6 | `NotesListWindow.xaml`/`SettingsWindow.xaml` 하드코딩 색 → DynamicResource 치환(§3.2 표 그대로) | **저** | Sonnet | 1:1 대응표 제공, 실패해도 색 오류로 국소 |
| T7 | `NoteWindow.xaml` 색상 피커 Popup 2색 치환 | **저** | Sonnet | 단일 지점 |
| T8 | 설정 UI: 테마 콤보 + 재시드 버튼(XAML+VM), 기존 Language 패턴 복제 | **저** | Sonnet | 파일 내 선례 복제 |
| T9 | `WhatsNewWindow` XAML/코드비하인드(렌더 호출은 T5 산출물 사용) | **저** | Sonnet | 신규 독립 창, 실패 격리(try/catch) |
| T10 | 샘플 노트 3장 마크다운 본문 작성 + resx en/ko 15키 + `Strings.cs` 접근자 | **저** | Sonnet | 콘텐츠 작업 |
| T11 | `editor.html` 다크 CSS + `editor.js` theme 파라미터 + 번들 재빌드, `MarkdownWysiwyg` URL 파라미터 | 중 | Opus 권장 | 번들-소스 CI 일치 검증·보안 CSP 불변 확인 필요(변경 자체는 작음) |
| T12 | 테스트 추가(§5) | 중 | Opus 작성·Sonnet 보조 | 게이트 로직 검증이 본질 |
| T13 | 문서: CHANGELOG 2.3.0 섹션, README 양측 스크린샷 갱신 | **저** | Sonnet | 문서 규칙(선행 갱신) 준수 |

권장 순서: T1 → T6·T7(치환) → T2 → T3·T4 → T5·T11 → T8·T9·T10 → T12 → T13. T6~T7은 T1의 사전 스타일 검증(데모)과 병렬 가능.

---

## 5. 테스트·검증 계획

### 5.1 단위 테스트 (`stickypad.Tests/Tests.cs`에 추가, 현행 107케이스 유지 + 신규)
- **테마 해소**: `ThemeService.Resolve("system"/"light"/"dark", systemIsDark)` 순수 함수로 분리해 4조합 검증. 알 수 없는 값(`"blue"`, null) → Light 폴백.
- **게이트 매트릭스** (판정 로직을 `internal static`으로 분리해 창 없이 테스트):

| 케이스 | LoadedFromFile | LastSeenVersion | 노트 수 | 파일 인자 | 기대 |
|---|---|---|---|---|---|
| 신규 설치 | false | null | 0 | 없음 | 투어 O / WN X / LastSeen 기록 |
| 업그레이드 | true | 2.2.2 | n | 없음 | 투어 X / WN O |
| 최초 도입 업그레이드 | true | null | n | 없음 | 투어 X / WN O |
| 동일 버전 재실행 | true | 2.3.0 | n | 없음 | 둘 다 X |
| 다운그레이드 | true | 9.9.9 | n | 없음 | 둘 다 X, 기록 갱신 |
| 업그레이드+파일 더블클릭 | true | 2.2.2 | n | 있음 | WN 보류(LastSeen 갱신은 정책대로) |
- **ChangelogReader**: 정상 추출(중간 버전), 최신 섹션, 미존재 버전 → null, `v` 접두 태그 헤더, 마지막 섹션(다음 `## [` 없음), 빈 입력.
- **OnboardingService**: 빈 인메모리 저장소에 시드 → 노트 3개·`Format=Markdown`·`Tags` 비어있지 않음·`PlainText` 채워짐; 노트 존재+force=false → no-op; force=true → 추가 생성; 시드 후 `OnboardingCompleted=true`.
- **HtmlRenderer**: `IsDark` 경계값(Gray `#373A40` → dark, Yellow `#FEF3B0` → light); 라이트 5색 입력 시 기존 출력과 **문자열 동일**(golden 비교 — 회귀 잠금); 다크 입력 시 `rgba(255,255,255` 포함·`#1565C0` 미포함.
- **AppSettings 직렬화**: 구버전 JSON(신규 키 없음) 역직렬화 → 기본값(`"system"`, null, false); 라운드트립 보존.

### 5.2 스크린샷 하네스 (두 테마)
- 프로젝트 메모리의 기법을 따른다: **격리 데모 DB + RenderTargetBitmap 자기 캡처**(화면 그랩 금지), WebView2 표면은 `CapturePreviewAsync`. 실행 중 앱/실DB를 건드리지 않는다.
- 캡처 매트릭스(라이트/다크 × 4): ① NotesListWindow(카드 선택·태그 칩·휴지통 탭 포함 상태), ② SettingsWindow(테마 콤보 노출), ③ WhatsNewWindow, ④ Gray 노트의 미리보기(다크 렌더 분기 확인). → `docs/screenshots/`에 `*-light.png`/`*-dark.png`로 저장, README 갱신 시 crop 사용.
- 수동 체크리스트: 다크에서 타이틀바 어두움(NotesList/Settings/WhatsNew), 트레이 컨텍스트 메뉴·노트 ⋯ 메뉴 다크, 설정 저장 즉시 열린 목록 창 색 전환, OS 테마 전환(system 모드) 실시간 반영, MessageBox는 라이트로 남음(알려진 한계 확인).

### 5.3 배포 형상 검증 (필수 — 메모리 규칙)
- `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true` 산출 단일 exe로: ① What's New가 CHANGELOG 섹션을 표시(임베드 확인), ② 신규 설치 시나리오(임시 `%LOCALAPPDATA%\StickyPad` 리다이렉트 또는 클린 VM)에서 투어 3장 생성, ③ WYSIWYG 다크 파라미터 동작. **Debug 실행 확인만으로 완료 처리 금지.**
- 테스트 전체(`dotnet test`) 통과를 릴리즈 게이트로(기존 CI 게이트 활용).

---

## 6. 예상 규모와 릴리즈 버전

| 항목 | 추정 |
|---|---|
| 신규 파일 | 9개 내외 — `ThemeService(.I)`, `OnboardingService(.I)`, `ChangelogReader`, `AppVersion`, `WindowThemeHelper`, `WhatsNewWindow.xaml(.cs)`, `Themes/AppColors.Light/Dark.xaml` |
| 수정 파일 | 12개 내외 — `App.xaml(.cs)`, `AppSettings`, `SettingsService(.I)`, `SettingsViewModel`, `SettingsWindow.xaml`, `NotesListWindow.xaml`, `NoteWindow.xaml(.cs)`, `HtmlRenderer`, `MarkdownWysiwyg`, `UpdateService`(버전 헬퍼 위임), `WindowManager(.I)`, csproj, `Strings.cs`+resx 2, `editor.html`/`editor.js`(+번들) |
| 순증 LOC | 프로덕션 ~900, 테스트 ~350, 리소스/콘텐츠 ~150 |
| 작업량 | 고위험 트랙(T1~T5, T11) 1.5~2일 + 저위험 병렬 트랙 0.5~1일 + 검증 0.5일 |
| 릴리즈 버전 | **2.3.0** (기능 추가·설정 하위 호환 → minor). 태그 전 CHANGELOG 2.3.0 섹션 필수 — What's New가 그 섹션을 그대로 표시하므로 사용자 대면 문구 품질로 작성할 것 |
| 롤백 안전성 | 설정 신규 3키는 구버전이 무시(Json 역직렬화 관용) — 다운그레이드 시 부작용 없음. 샘플 노트는 일반 노트라 데이터 스키마 영향 없음 |

### 미해결 질문 (구현 착수 전 확인)
1. WPF-UI 3.0.5의 `ApplicationThemeManager.Apply` 시그니처·표준 컨트롤 스타일의 DynamicResource 여부 — T1 첫 단계에서 데모로 확정(불일치 시 1층 키를 2층 자체 사전으로 흡수하는 폴백 설계 유지).
2. What's New의 영어 사용자 경험(한국어 CHANGELOG 노출) 수용 여부 — 수용 불가 판단 시 resx 요약 하드코딩 방식(릴리즈당 3~5불릿)으로 전환하는 대안 존재(T4 파서는 그대로 재사용 가능).
3. 샘플 노트 본문 최종 문안 — T10에서 초안 후 리뷰 1회.
