# Changelog

All notable changes to StickyPad are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This file starts from the point it was introduced — earlier history lives in the git log.

## [2.2.0] - 2026-07-05

### Added
- 위지윅 편집에 **서식 툴바와 단축키** 추가 — 마크다운 문법을 직접 입력하지 않고, 버튼(굵게·기울임·취소선·인라인 코드·제목·글머리표/번호/체크박스 목록·인용·링크)이나 단축키(`Ctrl+B`/`Ctrl+I`, `Ctrl+Shift+X`, `` Ctrl+` ``, `Ctrl+K`, `Ctrl+Alt+1~3` 등)로 서식을 적용한다. 워드프로세서처럼 편집하면서 저장은 여전히 순수 마크다운 소스라 볼트(`.md`)와 왕복 호환된다.

## [2.1.1] - 2026-07-05

### Fixed
- 설치본(단일 파일 배포)에서 위지윅(WYSIWYG) 편집이 "지정된 경로를 찾을 수 없습니다"로 시작하지 못하던 문제 — 에디터 자산(CodeMirror 번들·호스트 HTML)을 어셈블리에 임베드하고 런타임에 로컬 폴더로 추출하도록 변경해 단일 exe에서도 동작하도록 수정.

## [2.1.0] - 2026-07-05

### Added
- **위지윅(WYSIWYG) 마크다운 편집** — 마크다운 노트에서 ✎ 토글로 라이브 프리뷰 편집. 커서가 없는 줄은 마크다운 마커(`#`·`**`·`` ` ``·`>`)가 숨겨져 서식으로 보이고, 편집 중인 줄만 원본을 노출한다(Obsidian식). 오프라인 CodeMirror 6 기반이며, 저장은 순수 마크다운 소스라 볼트(`.md`)와 왕복 호환된다. 설정으로 기본값을 지정할 수 있고 원본 소스 편집으로 언제든 폴백된다.

### Security
- 위지윅 편집은 편집 전용 WebView2에만 스크립트/웹메시지를 허용하고(렌더 미리보기는 스크립트 OFF 유지), 네트워크를 전면 차단(CSP `default-src 'none'`, 가상 호스트 `DenyCors`, 페이지 밖 이동·새 창 차단)하며, 노트 내용은 문자열 데이터로만 주고받아 코드 실행 경로를 만들지 않는다.

## [2.0.1] - 2026-07-05

### Fixed
- 볼트로 이관할 때 휴지통·빈 노트까지 `.md`로 저장되던 문제 — 활성·비어있지 않은 노트만 이관하도록 수정(볼트 폴더가 노트 목록과 일치).
- 템플릿에서 새 노트를 만들거나 파일을 열 때 빈 자리표시 노트가 하나 더 남던 문제 — 실제 노트를 띄울 때 빈 노트를 정리.
- 트레이 메뉴의 폴더 선택 대화상자(볼트 가져오기·내보내기 등)가 소유자 창 없이 즉시 사라지던 문제 — 항상 유효한 소유자 창을 부여.

## [2.0.0] - 2026-07-05

### Added
- **Vault mode (live).** Switch the storage backend to a "Vault folder (.md files)" in **Settings → Storage**. Each note is saved as a human‑readable `.md` file (YAML front matter with `id`/`title`/`format`/`color`/`tags`/dates, plus the body — Obsidian‑compatible), while StickyPad‑specific state (window geometry, trash, etc.) lives in a sidecar `.stickypad-index.json`. Put the folder in OneDrive/Dropbox/etc. and your notes sync across machines (changes made externally are picked up on restart). On first switch, existing notes are safely migrated into the vault (only when the vault is empty). The built‑in database remains the default — Vault mode is strictly opt‑in, with zero risk to existing users.
- **Vault export/import** (tray → *Vault*) — export all notes to a folder of round‑trippable `.md` files, or import a folder of `.md` files back into StickyPad.
- **winget manifest** — StickyPad can now be packaged/submitted via winget (see `packaging/winget/`).
- **Install & SmartScreen guide** (`docs/INSTALL.md`) — explains why the unsigned‑app SmartScreen warning appears and how to verify the release's SHA‑256 checksum.

### Changed
- Introduced a DB schema migrator under the hood, enabling safe, versioned data‑model evolution going forward.

## [1.7.0] - 2026-07-05

### Added
- **Selection-based bulk actions in the All‑notes window.** Notes can now be checked and, via a "일괄 작업 ▾" (Bulk actions) menu, bulk‑deleted (moved to Trash) or bulk‑recolored in one step.
- **Note templates.** New notes can be created from a built‑in template (Daily log / Meeting notes / To‑do list), available from a note's **⋯** More menu and the tray's *New note from template* submenu. Templates substitute `{{date}}` with today's date.
- **Toolbar overflow "⋯" menu.** The note header's less‑used buttons (new note, open file, all notes, export/print, templates) are consolidated into a single **⋯** menu, fixing toolbar clipping at narrow note widths.

### Security
- **Auto‑update integrity verification.** The updater now verifies the downloaded exe against a published SHA‑256 checksum (a `.sha256` release asset produced by CI) before applying it, and refuses to apply on mismatch or missing checksum (fail‑closed).

## [1.6.1] - 2026-07-04

### Security
- **백업 가져오기 XAML RCE 하드닝.** 가져온 노트의 `RichTextXaml` 콘텐츠를 `PlainText` 로 강등(`SanitizeImportedNote`)해, 신뢰할 수 없는 XAML이 WPF의 비제한 파서(`TextRange.Load`)에 도달하지 못하게 했다. `NoteWindow.LoadEditorContent` / `TextExtraction.ToPlainText` 두 XAML 싱크에도 위험 마커 가드(`ContainsDangerousXaml`)를 추가해 심층 방어했다.
- **가져온 노트의 `LinkedFilePath` 제거.** 편집 시 임의 경로에 노트 내용을 조용히 써버리는(임의 파일 덮어쓰기) 경로를 차단했다.
- **WebView2 `NewWindowRequested` 스킴을 http/https 로 제한.** 노트 마크업의 `file:`/UNC/프로토콜 링크로 로컬 실행을 유도하는 것을 막았다.
- **내보내기 문서 CSP 에서 원격 리소스 차단.** HTML/PDF/인쇄 문서는 `data:` 이미지·미디어·폰트만 허용해 추적/SSRF 위험을 완화했다.
- **명명 파이프(`StickyPad.OpenFile.v1`) 입력 검증.** 커맨드라인과 동일하게 파일 존재·지원 형식 검사 + 최대 20개 제한을 적용했다.
- **자동 업데이트 태그·URL 검증 및 스테이징 강화.** GitHub 릴리즈 태그 형식 검증, 다운로드 URL을 https + GitHub 호스트로 제한, 예측 불가능한 GUID 스테이징 폴더 사용.
- 보안 회귀 테스트를 추가했다. 자세한 내용은 [`docs/SECURITY-REVIEW.md`](docs/SECURITY-REVIEW.md) 참고.

## [1.6.0] - 2026-07-04

### Added
- **Selection-based export in the All‑notes window.** Each note card now has a checkbox; export targets the checked notes, or the current searched/filtered list if none are checked. Selection persists across search/filter changes, with a live "N개 선택됨" count and *전체 선택* / *선택 해제* buttons. Checkboxes/selection apply to the active view only (not the Trash tab).
- **"HTML 문서로…" export** — renders the target notes into a single styled `.html` document: each note gets a left accent bar in its note color, Markdown/HTML notes render with their real formatting, and plain/rich‑text notes render as text. The document ships with a strict Content-Security-Policy that blocks scripts.
- **"PDF로…" export** — prints the same generated HTML document to a single `.pdf` using an offscreen WebView2 instance (the same WebView2 runtime already used for note preview).
- **"노트별 Markdown 파일로…" export** — prompts for a destination folder and writes one `.md` file per note, named from the note title (sanitized, with reserved Windows names handled and filenames de‑duplicated so existing files are never overwritten). Each file includes YAML front matter with `title`, `tags`, `color`, `created`, and `modified`.
- Export operations report success/failure back to the user after completing.
- **Export/print the current note** — the note window's header **⤓** menu can now save the open note as PDF, HTML, or Markdown, or send it straight to the printer.
- **Bilingual UI (English / Korean)** — the app auto-detects the Windows display language on first run and can be switched manually in **Settings** (takes effect after restart).
- **Korean README** (`README.ko.md`), linked from the English README.

### Changed
- The All‑notes window's *내보내기* control is now a *내보내기 ▾* dropdown offering the three formats above, replacing the previous single text-dump behavior for that button.

The tray menu's *Export backup…* / *Import backup…* (portable JSON) and *Export notes as text…* (single-file Markdown/`.txt` dump of all active notes) are unchanged.
