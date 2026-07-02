# StickyPad

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
- 🏷️ **Tags & search** — type `#tag` inside a note; filter and full‑text search from the **All notes** window with match highlighting.
- 🔗 **Wiki links** — write `[[Note title]]` to link notes together; click to jump.
- 🗑️ **Safe deletes** — a Recycle Bin keeps deleted notes for 30 days before auto‑purging.
- 🖥️ **Stays out of the way** — tray icon, per‑note opacity, always‑on‑top pin, multi‑monitor aware.
- 💾 **Local‑first** — an embedded LiteDB file on your machine, with one‑click JSON backup export/import.

## Screenshots

| Desktop notes | All notes (search • tags • trash) | In‑note editor |
| :---: | :---: | :---: |
| ![Desktop notes](docs/images/notes-desktop.png) | ![All notes window](docs/images/all-notes.png) | ![Note editor toolbar](docs/images/note-editor.png) |

> Screenshots live in [`docs/images/`](docs/images). Replace the placeholders with your own captures anytime.

## Features

### Notes & editing
- Rich‑text editor (WPF `RichTextBox`) with a toolbar that appears on hover/focus and hides in preview mode.
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
- **Single instance** — launching again just surfaces the running app.

### Data & backup
- **Backup export/import** to a portable JSON file (tray menu).
- **Export notes as text** — dump all active notes to a readable Markdown/`.txt` file (tray menu or the All‑notes window's *내보내기* button).
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

> Alignment, lists, task items, code blocks, font size, color, opacity, and pin are available from the on‑hover toolbar.

## Getting started

### Download
Grab the latest build from the [**Releases**](https://github.com/BaeTab/stickypad_windows/releases) page, unzip, and run `StickyPad.exe`.

Requires the **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** (x64) unless you use a self‑contained build (see below).

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
- **Recolor / pin / fade:** use the note's hover toolbar to switch color, pin it on top, or adjust opacity.
- **Delete safely:** deleting moves a note to the **Trash** tab; restore it there, or empty the bin. Anything left 30 days is purged automatically.
- **Back up:** tray menu → *Export backup…* writes a JSON file; *Import backup…* restores it (notes with the same id are overwritten).

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
- Markdown rendering in preview mode
- Image paste / attachments
- Note grouping and reminders
- Cloud sync (optional)

## License

Released under the [MIT License](LICENSE) © 2026 BaeTab. You're free to use, modify, and distribute it — see the LICENSE file for details.

## Acknowledgements

Thanks to the maintainers of WPF‑UI, CommunityToolkit.Mvvm, LiteDB, Serilog, H.NotifyIcon, and NHotkey — StickyPad stands on their shoulders.
