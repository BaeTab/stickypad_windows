# Changelog

All notable changes to StickyPad are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This file starts from the point it was introduced — earlier history lives in the git log.

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
