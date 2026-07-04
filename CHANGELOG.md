# Changelog

All notable changes to StickyPad are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This file starts from the point it was introduced — earlier history lives in the git log.

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
