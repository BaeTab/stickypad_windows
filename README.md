# StickyPad

**English** | [한국어](README.ko.md)

> 📝 A lightweight, fast sticky‑note app for Windows — instant capture, rich text, tags, and wiki‑style links, all stored locally.

StickyPad lives in your system tray and puts a note on your desktop in a single keystroke. Notes are colorful, resizable, always‑on‑top‑capable, and searchable. Everything is saved locally in an embedded database — no account, no cloud, no telemetry.

![StickyPad notes on the desktop](docs/images/notes-desktop.png)

<sub>Built with **.NET 8 · WPF · MVVM**. Windows 10/11, x64.</sub>

---

## Table of contents

- [Highlights](#highlights)
- [Screenshots](#screenshots)
- [Features](#features)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Getting started](#getting-started)
  - [Download](#download)
  - [Build from source](#build-from-source)
- [Usage guide](#usage-guide)
- [Where your data lives](#where-your-data-lives)
- [Architecture](#architecture)
- [Privacy](#privacy)
- [Roadmap](#roadmap)
- [License](#license)
- [Acknowledgements](#acknowledgements)

---

## Highlights

- ⚡ **Instant capture** — `Ctrl+Shift+N` drops a new note anywhere, even when no window is focused.
- 🎨 **Six color themes** — Yellow, Pink, Blue, Green, Purple, and a dark Gray.
- ✍️ **Rich text** — bold, italic, underline, strikethrough, lists, task checkboxes, inline code, code blocks, alignment, and per‑selection font sizes.
- 🧩 **Renders Markdown & HTML** — flip a note to Markdown or HTML mode and it renders live (via WebView2).
- 🏷️ **Tags & search** — type `#tag` inside a note; filter and full‑text search from the **All notes** window with match highlighting.
- 🔗 **Wiki links** — write `[[Note title]]` to link notes together; click to jump.
- 📂 **Open & link `.md` files** — open a Markdown/text file from disk (double‑click, drag‑drop, or `Ctrl+O`) and StickyPad renders it in a note. Edits save straight back to the file, and external changes reload automatically — a two‑way link, not a copy.
- 🗑️ **Safe deletes** — a Recycle Bin keeps deleted notes for 30 days before auto‑purging.
- 🖥️ **Stays out of the way** — tray icon, per‑note opacity, always‑on‑top pin, multi‑monitor aware.
- 💾 **Local‑first** — an embedded LiteDB file on your machine, with one‑click JSON backup export/import.
- 🌐 **Bilingual UI** — English & Korean; auto-detects your Windows language and switchable in Settings.

## Screenshots

### Live rendering — Text · Markdown · HTML
Switch a note between **Text / Markdown / HTML** and it renders on the spot:

![Text, Markdown and HTML rendering](docs/images/render-demo.gif)

| Desktop notes | All notes (search • tags • trash) | In‑note editor |
| :---: | :---: | :---: |
| ![Desktop notes](docs/images/notes-desktop.png) | ![All notes window](docs/images/all-notes.png) | ![Note editor toolbar](docs/images/note-editor.png) |

> Screenshots live in [`docs/images/`](docs/images). Replace the placeholders with your own captures anytime.

## Features

### Notes & editing
- Rich‑text editor (WPF `RichTextBox`) with a toolbar that appears on hover/focus and hides in preview mode.
- **Text / Markdown / HTML modes** — pick a mode per note from the header (T / M / &lt;/&gt;). Markdown (rendered with [Markdig](https://github.com/xoofx/markdig)) and raw HTML render in a themed **WebView2** preview; `Ctrl+E` toggles source ⇄ rendered, and links open in your browser.
- **Formatting:** bold, italic, underline, strikethrough, bulleted & numbered lists, left/center/right alignment, inline code and code blocks, and a per‑selection font‑size picker.
- **Task checkboxes** — insert inline `☐` items you can tick right inside the note.
- **Preview / Edit toggle** (`Ctrl+E`) locks a note read‑only so you can't fumble the text while reading it.
- **Auto title** — the first non‑empty line becomes the note's title (used for the list and for `[[links]]`).
- **Autosave** — edits are debounced and persisted automatically; no save button.

### Organization
- **`#tags`** are parsed live from the note body; the **All notes** window shows a tag sidebar with counts and one‑click filtering.
- **Search** across title, tags, and body with scored ranking and inline highlighting of matches.
- **`[[Wiki links]]`** resolve by note title — clicking opens (or focuses) the target note; missing targets are reported.
- **Auto‑linked URLs** — `http(s)://…` addresses become clickable and open in your browser.

### Linked files (open `.md` from disk)
- **Open a Markdown/text file** three ways: double‑click it (once StickyPad is set as its *Open with* app), **drag‑drop** it onto any note, or press **`Ctrl+O`** — it opens as a rendered note.
- **Two‑way sync** — the note *is* the file: your edits are written back to the original `.md`, and if the file changes in another editor the note reloads automatically (with a conflict prompt if you had unsaved edits).
- **Non‑destructive** — deleting/trashing a linked note only removes the note; the **original file is never deleted**. Re‑opening the same file reuses the existing note instead of duplicating it.
- **Safe by design** — reading detects the file's encoding (BOM‑aware); writing is UTF‑8. The file path shows on the header hover tooltip.

### Window behavior
- Move, resize (grip), and recolor each note; position/size/color/opacity persist per note.
- **Per‑note opacity** (50–100%) and an **always‑on‑top** pin.
- **Multi‑monitor safe** — notes that would open off‑screen are nudged back into view on startup.
- Closing a note with content **hides** it (kept alive in the tray); closing an empty note discards it.

### System integration
- **System‑tray icon:** left‑click toggles show/hide‑all, double‑click makes a new note, right‑click opens the menu.
- **Settings window** — toggle auto‑start, enable/disable global hotkeys, and **rebind the hotkeys** with a click‑and‑press capture field.
- **Global hotkeys:** new note and All‑notes list from anywhere, fully configurable (or disabled).
- **Start with Windows** — optional auto‑start via the current‑user `Run` registry key.
- **Automatic updates** — on launch (and from the tray's *Check for updates…*) StickyPad checks GitHub Releases and, with your OK, downloads the new build and self‑replaces on restart. Toggle it in Settings.
- **Single instance** — launching again just surfaces the running app (and forwards any file passed on the command line to it, so double‑clicking an `.md` opens it in the running instance).
- **`.md` file association** — StickyPad registers itself (per‑user, no admin) in the Windows *Open with* list for `.md`/`.markdown`, without hijacking your default editor.
- **Language** — English or Korean UI; follows Windows by default, change it in **Settings** (takes effect after restart).

### Data & backup
- **Backup export/import** to a portable JSON file (tray menu).
- **Export notes as text** — dump all active notes to a readable Markdown/`.txt` file (tray menu).
- **Export from the All‑notes window** — the *내보내기 ▾* button exports the selected notes (or the current filtered list, if nothing is selected) as a single styled **HTML** document, a single **PDF** (rendered via an offscreen WebView2), or **one Markdown file per note** (with YAML front matter) into a folder you choose.
- **Export/print a single note** — from a note's header **⤓** menu, save the current note as PDF, HTML, or Markdown, or send it to the printer.
- **Recycle Bin** — deleted notes are soft‑deleted, restorable, and permanently purged after 30 days (a cleanup also runs at startup).
- **Rolling logs** kept for 7 days for troubleshooting.

## Keyboard shortcuts

**Global (system‑wide)** — defaults; rebind them in **Settings**:

| Shortcut | Action |
| --- | --- |
| `Ctrl` + `Shift` + `N` | New note |
| `Ctrl` + `Shift` + `L` | Open the **All notes** window |

**Inside a note:**

| Shortcut | Action |
| --- | --- |
| `Ctrl` + `B` / `I` / `U` | Bold / Italic / Underline |
| `Ctrl` + `Shift` + `X` | Strikethrough |
| `` Ctrl + ` `` | Inline code |
| `Ctrl` + `E` | Toggle Preview / Edit (read‑only lock) |
| `Ctrl` + `O` | Open a Markdown/text file from disk (linked, two‑way) |

> Alignment, lists, task items, code blocks, font size, color, opacity, and pin are available from the on‑hover toolbar.

## Getting started

### Download
Grab the latest build from the [**Releases**](https://github.com/BaeTab/stickypad_windows/releases) page, unzip, and run `StickyPad.exe`.

Requires the **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** (x64) unless you use a self‑contained build (see below). The Markdown/HTML preview uses the **[WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/)** — preinstalled on Windows 11 (and most Windows 10).

### Build from source

**Prerequisites**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 (WPF is Windows‑only)

**Clone & run**
```bash
git clone https://github.com/BaeTab/stickypad_windows.git
cd stickypad_windows
dotnet run --project stickypad
```

**Publish a distributable exe**
```bash
# Framework-dependent single file (small; needs the .NET 8 Desktop Runtime)
dotnet publish stickypad -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish/win-x64

# Or fully self-contained (larger; no runtime needed on the target PC)
dotnet publish stickypad -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o publish/win-x64-selfcontained
```

Open `stickypad.sln` in Visual Studio 2022 (17.8+) if you prefer the IDE.

## Usage guide

- **Make a note:** press `Ctrl+Shift+N`, or double‑click the tray icon.
- **Tag it:** type `#idea`, `#todo`, `#work` anywhere in the body — tags appear in the All‑notes sidebar.
- **Link notes:** write `[[Project ideas]]`; it becomes a clickable link that opens the note titled "Project ideas".
- **Find anything:** press `Ctrl+Shift+L`, type in the search box — title, tags, and body are searched with highlights.
- **Export a batch of notes:** in the All‑notes window, tick the checkbox on each note card you want (selection persists across search/filter, with a "N개 선택됨" count and *전체 선택*/*선택 해제*), then *내보내기 ▾* → **HTML 문서로…**, **PDF로…**, or **노트별 Markdown 파일로…**. With nothing ticked, it exports the currently filtered list instead.
- **Recolor / pin / fade:** use the note's hover toolbar to switch color, pin it on top, or adjust opacity.
- **Delete safely:** deleting moves a note to the **Trash** tab; restore it there, or empty the bin. Anything left 30 days is purged automatically.
- **Back up:** tray menu → *Export backup…* writes a JSON file; *Import backup…* restores it (notes with the same id are overwritten).

### Markdown & HTML modes

Each note can be plain **Text**, **Markdown**, or **HTML**. Pick the mode from the buttons in the note's header:

> `T` = Text (rich) · `M` = Markdown · `</>` = HTML · `👁` = Preview / Edit toggle

**Steps to render:**
1. Click **`M`** (Markdown) or **`</>`** (HTML) to switch the note's mode.
2. The note becomes a **live split** — **source on top, rendered preview below** — and the preview updates as you type. Drag the divider to resize.
3. Press **`👁` (Eye)** or **`Ctrl+E`** to expand to a **full‑screen rendered view**, and again to return to the split.

When you **reopen** a Markdown/HTML note later, it opens in the same live split.

- Links in a rendered note open in your **default browser**.
- The preview is themed to match the note's color.
- Rendering uses the **WebView2 runtime** (built into Windows 11 and most Windows 10).

> **Pasting:** in Markdown/HTML mode, pasted content is inserted as **plain text automatically**, so your tags and markup are kept exactly as copied (even from a browser or Word).

### Open & link a `.md` file from disk

Point StickyPad at a Markdown/text file on your machine and it opens as a **rendered, live‑linked** note:

- **Drag‑drop** the `.md` onto any note, **press `Ctrl+O`** / the 📂 header button, or use the tray menu → *Open markdown file…*
- **Double‑click** from Explorer/desktop: right‑click the file → *Open with* → **StickyPad** (StickyPad registers itself in that list on first run; choose *Always* to make double‑click open it).

Once open:

1. The note shows the **rendered Markdown** first; press **`Ctrl+E`** / the 👁 button to edit the source.
2. **Your edits are saved back to the original file** automatically (debounced) — the note and the file stay in sync.
3. If the file is **changed by another program**, the note reloads it. If you had unsaved edits, StickyPad asks whether to keep yours or take the disk version.
4. **Deleting the note keeps the file.** It only unlinks; your `.md` on disk is untouched.

> The header's hover tooltip shows the linked file's full path. Supported: `.md`, `.markdown`, `.txt`, and similar text files.

## Where your data lives

Everything is stored under your local profile:

```
%LocalAppData%\StickyPad\
├─ notes.db         # LiteDB embedded database (all notes + trash)
├─ settings.json    # preferences (hotkeys, auto-start, etc.)
└─ logs\
   └─ app-YYYYMMDD.log   # rolling logs, 7-day retention
```

To move StickyPad to a new machine, copy `notes.db` (or use *Export backup…*).

## Architecture

A small, layered WPF application using the MVVM pattern and dependency injection.

**Tech stack**
- **UI:** WPF (.NET 8) + [WPF‑UI](https://github.com/lepoco/wpfui)
- **MVVM:** [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- **Host & DI:** `Microsoft.Extensions.Hosting` / `DependencyInjection`
- **Storage:** [LiteDB](https://www.litedb.org/) (embedded NoSQL)
- **Rendering:** [Markdig](https://github.com/xoofx/markdig) (Markdown) + [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) (HTML preview)
- **Logging:** [Serilog](https://serilog.net/) (file + debug sinks)
- **Tray:** [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon)
- **Hotkeys:** [NHotkey.Wpf](https://github.com/thomaslevesque/NHotkey)

**Project layout**
```
stickypad/
├─ App.xaml(.cs)        # startup, DI host, single-instance, global hotkeys
├─ Models/              # Note, AppSettings, NoteColor/Theme, content format
├─ ViewModels/          # NoteViewModel, NotesListViewModel (+ summaries)
├─ Views/               # NoteWindow, NotesListWindow (XAML + code-behind)
├─ Services/            # repository, settings, tray, window manager,
│                       #   hotkeys, auto-start, backup (interface + impl)
└─ Utils/               # text extraction, search matcher, converters, icons
```

**Notable design points**
- **Debounced autosave** in `NoteViewModel` coalesces rapid edits before hitting the DB.
- `SaveContentAsync` deliberately preserves trash state so a closing window's stale copy can't "undelete" a note (a subtle bug this codebase guards against explicitly).
- The **`WindowManager`** owns note‑window lifetime, off‑screen correction, and rebuild‑from‑DB on import/restore.
- The tray icon is created synchronously (`ForceCreate`) to avoid an async icon path that could leave the tray empty.

## Privacy

StickyPad is **local‑first and offline**. There is no account, no network calls, and no analytics. Your notes never leave your machine unless *you* export a backup file.

## Roadmap

Ideas under consideration (contributions welcome):
- Live split preview (edit + rendered side by side)
- Image paste / attachments
- Note grouping and reminders
- Cloud sync (optional)

## License

Released under the [MIT License](LICENSE) © 2026 BaeTab. You're free to use, modify, and distribute it — see the LICENSE file for details.

## Acknowledgements

Thanks to the maintainers of WPF‑UI, CommunityToolkit.Mvvm, LiteDB, Serilog, H.NotifyIcon, and NHotkey — StickyPad stands on their shoulders.
