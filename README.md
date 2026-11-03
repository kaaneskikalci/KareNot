<img src="karenot.png" width="96" align="right" alt="KareNot logo">

# KareNot

*English · [Türkçe](README.tr.md)*

[![CI](https://github.com/kaaneskikalci/KareNot/actions/workflows/ci.yml/badge.svg)](https://github.com/kaaneskikalci/KareNot/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/kaaneskikalci/KareNot)](https://github.com/kaaneskikalci/KareNot/releases)

**KareNot** ("frame note" in Turkish) is a Windows tool for reviewing game playtest recordings. While you watch an OBS
recording you mark bugs and change requests with a timestamp, a screenshot (optionally with the problem area framed in
red) or a short clip, and hand them over as tasks to teammates and AI coding agents. The same session produces cut notes
for the video editor: bleeps for swearing and cut ranges, as machine-readable JSON, as DaVinci Resolve markers, or as a
rendered first cut.

It was built for the playtests of indie games such as KILLSTREAM. The user interface is in Turkish; the
[Turkish guide](README.tr.md) has the full walkthrough.

> 📷 **Screenshot placeholder** — main window with a recording open, the game test notes on the right and markers on the
> timeline. Planned file: `docs/images/main-window.png`
>
> 🎞️ **GIF placeholder** — drag on the video to frame an area, type a note, press `B`; the note appears in the list with
> its screenshot. Planned file: `docs/images/add-bug.gif`

<!-- When the images exist, replace the placeholders above with:
![Main window](docs/images/main-window.png)
![Adding a bug with a framed area](docs/images/add-bug.gif)
-->

## Features

- **Game test notes** — `BUG`, `DEĞİŞİKLİK` (change request) and `NOT` (note) at the current frame, with a screenshot,
  a red frame around a dragged area, status (open / fixed / retest), severity (low / medium / critical), `#tags`,
  search and filters, note templates, undo / redo.
- **Clips** — a short H.264 clip around a note (default 5 s before and after), made with ffmpeg.
- **Editing marks** — bleeps (`K`, hold for a range) and cut start / end (`1` / `2`), shown as ranges on the timeline;
  watch the video with the cuts skipped (`X`).
- **Assisted editing** — profanity candidates found offline with [whisper.cpp](https://github.com/ggml-org/whisper.cpp)
  and a word list, dead-air candidates (long silent or frozen stretches) found with ffmpeg.
- **Review workflow** — resume where you stopped, "reviewed up to here", a per-folder video list with progress, build and
  tester info, jumping between marks, looping around a note, frame preview over the seek bar.
- **Sharing** — Markdown + HTML report, task list for AI agents and developers, findings package (ZIP), CSV, editing
  notes (JSON + Markdown), DaVinci Resolve marker EDL, and a bleeped and cut copy of the video.

## Requirements

| | |
|---|---|
| OS | Windows 10 / 11, x64 |
| Runtime | [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) — Windows offers the download link on first start if it is missing |
| Player | [VLC media player 64-bit](https://www.videolan.org/vlc/) — KareNot uses the installed VLC's engine (libvlc 3) |
| Optional | [ffmpeg](https://ffmpeg.org/) for clips, previews, exports and dead-air detection: `winget install Gyan.FFmpeg` |
| Optional | whisper.cpp `whisper-cli.exe` and a **multilingual** ggml model (e.g. `ggml-small.bin`) for profanity candidates |

## Install

1. Install VLC (64-bit) and, if Windows asks for it, the .NET 10 Desktop Runtime.
2. Download `KareNot-<version>-win-x64.zip` from [Releases](https://github.com/kaaneskikalci/KareNot/releases), extract it
   to a folder you can write to (not `Program Files`) and run `KareNot.exe`.
   The exe is not signed: on the SmartScreen warning choose *More info → Run anyway*.
   Each release also has the bare `KareNot.exe` (releases up to v1.4 have only that), so
   [`releases/latest/download/KareNot.exe`](https://github.com/kaaneskikalci/KareNot/releases/latest/download/KareNot.exe)
   always points to the newest exe.
3. Optional: set the ffmpeg and whisper paths in *Araçlar ▾ → Ayarlar* (Tools → Settings).

Notes are saved in a `KareNot Notlar` folder next to `KareNot.exe` (or `Desktop\KareNot` when that folder is not
writable). Another folder can be chosen in the settings; KareNot offers to move the existing notes there.

## Quick start

1. Open a recording: `Ctrl+O`, drag and drop it onto the window, or pass it to the exe.
2. Pause at a problem (`Space`), type what is wrong in the text box and press `B` (bug), `D` (change) or `Enter` (note).
   To point at something, drag a rectangle on the video first; the screenshot gets a red frame around it.
3. Select a note and press `C` for a clip, right-click for status and severity.
4. In the **Kurgu** (editing) tab, press `K` on swearing and `1` / `2` around parts to cut.
5. Press `Ctrl+S` for the report, or use *Dışa aktar ▾* (Export) for the task list, the ZIP or the editing notes.

## Keyboard shortcuts

All shortcuts work in every tab; letter shortcuts are ignored while typing in the note or search box. `F1` shows the full
list inside the app.

| Player | |
|---|---|
| `Space` | Play / pause |
| `←` `→` / `Shift`+`←` `→` | 3 s / 10 s back or forward |
| `,` `.` | One frame back / forward |
| `-` `+` | Playback speed (0.25× – 3×) |
| `↑` `↓`, mouse wheel on the video | Volume |
| `Page Up` / `Page Down` | Previous / next video in the folder |
| `F`, `F11`, double-click | Full screen (`Esc` leaves) |
| `Tab` | Show / hide the side panel |
| `Home` | Back to the start (a video otherwise resumes where you left it) |
| `[` `]` or `Ctrl`+`←` `→` | Previous / next mark of the open tab |
| `L` | Loop around the selected note |
| `I` | Reviewed up to here (near the end: video done) |
| `Ctrl+O` | Open a video |

| Game test | |
|---|---|
| `B` / `D` / `N` | Add BUG / DEĞİŞİKLİK / NOT (NOT needs text) |
| In the text box: `Enter`, `Ctrl+B`, `Ctrl+D`, `Ctrl+K` | Add as NOT / BUG / DEĞİŞİKLİK / bleep |
| Drag on the video | Freeze the frame and frame an area for the next note (`Esc` cancels) |
| `S` | Add the current frame to the selected note |
| `C` | Clip around the selected note (ffmpeg) |
| `Enter` | Pause and jump into the text box |
| `Ctrl+F` | Search (words or `#tags`) |

| Editing (Kurgu) | |
|---|---|
| `K` | Bleep; hold to make a range until you release |
| `1` / `2` | Cut start / cut end |
| `X` | Watch with the cuts skipped |

| List and files | |
|---|---|
| `G`, `Enter`, double-click | Go to the selected note (2 s before) |
| `F2` | Edit the selected note |
| `Del` | Delete the selected note (the last one if nothing is selected) |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+S` | Save the report (`rapor.md` + `rapor.html`) |
| `Ctrl+P` | Findings package (ZIP) |

## Files and export formats

Every video gets its own folder, named after the video file without its extension:

```
KareNot Notlar\
  inceleme.json                 resume position, review progress, build, tester (all videos)
  hazir_notlar.txt              note templates (editable)
  kufur_listesi.txt             words that make a bleep candidate (editable)
  <video name>\
    notlar.txt                  the notes of this video (source of truth)
    ss\                         screenshots and clips
    rapor.md, rapor.html        report
    cikti\                      exports
```

| Output | Where | Format |
|---|---|---|
| Notes | `<video>\notlar.txt` | UTF-8, tab-separated: `seconds  time  kind  note  video  screenshot  status  severity  end  clip`. Trailing empty columns are omitted; old 5- and 6-column files are read as they are. The first column (seconds, `.` decimals) is authoritative. Lines KareNot can't read are copied to `notlar_okunamayan.txt` instead of being dropped. |
| Report | `Ctrl+S` → `<video>\rapor.md` and `rapor.html` | Markdown and a self-contained dark HTML page with thumbnails and clips, grouped by kind |
| Task list | *Export → Görev listesi* → `cikti\agent_gorevleri_<video>.md` | One Markdown section per open game note: video, time, severity, status, build / tester, screenshot and clip links and absolute paths, and *Expected / Observed / Acceptance criteria* stubs |
| Findings package | `Ctrl+P` → `cikti\KareNot_<scope>_<yyyyMMdd_HHmm>.zip` | Report, CSV, task list, editing notes and the screenshots / clips, filtered by video, game notes only, open only |
| Editing notes | *Export → Kurgu notları* → `cikti\kurgu_notlari_<video>.json` + `.md` | JSON with `video`, `path`, `length_s`, `fps`, `bleeps[{start,end,text}]`, `cuts[{start,end,duration,text}]`, `cut_total_s`, `unpaired_cut_marks`, `notes`; times in seconds from the start |
| Resolve markers | *Export → DaVinci Resolve* → `cikti\<video>_isaretler.edl` | CMX-style marker EDL for *Timeline → Import → Timeline Markers from EDL*, timeline starting at 01:00:00:00, colored by kind |
| CSV | *Export → CSV* → `KareNot Notlar\cikti\KareNot_notlar_<stamp>.csv` | `;`-separated, UTF-8 with BOM, decimal comma (Turkish Excel); text starting with `=`, `+`, `@`, `-` is defused |
| Edited video | *Export → Bipli ve kesilmiş video* → `cikti\<video>_kurgu.mp4` | H.264 / AAC copy with 1 kHz bleeps over the bleep ranges and the paired cuts removed (ffmpeg trim + concat) |

## macOS: IINA plugin (EXPERIMENTAL, untested)

> ⚠️ **Experimental.** The IINA plugin has **never been run on a Mac**. Do not rely on it for notes you care about yet.

[`iina/`](iina/README.md) contains a plugin for the [IINA](https://iina.io) player that reads and writes the same note
format and folder layout. Its notes folder defaults to `~/Desktop/KareNot` and can be changed from the plugin menu
(*Not klasörünü seç…*); to share notes with Windows, point both apps at the same synced folder. The pure format code is
covered by `node iina/test/format_test.js` (run in CI). The issues found by a static review against IINA's source
(message handlers cleared by `loadFile`, `file.delete` / overwrite limits, message escaping, NFD file names) have been
fixed and smoke-tested against a fake IINA API only; whether the plugin works in IINA is unverified.

## Testing status

- Unit tests (xUnit, `tests/KareNot.Tests`) cover the note file format, time formatting, folder naming, cut / clip range
  math, every export format, the settings file, `inceleme.json` handling, whisper output parsing and the notes folder move.
  They run on every push in GitHub Actions (Windows).
- `KareNot.exe --selftest <log> <video>` drives the real window and player (see [CONTRIBUTING.md](CONTRIBUTING.md)).
- Not yet tried by hand: the export and tools menus end to end, changing the notes folder from the settings, display
  scaling above 100%, the IINA plugin (experimental, never run on a Mac).

## Building from source

```bash
dotnet publish KareNot.csproj -c Release -o yayin
```

Output: `yayin\KareNot.exe` (framework-dependent single file). Tests: `dotnet test tests/KareNot.Tests`. See
[CONTRIBUTING.md](CONTRIBUTING.md) for details and the release process, and [CHANGELOG.md](CHANGELOG.md) for the history.

## License

KareNot is released under the [MIT License](LICENSE). © 2026 Kaan Eskikalci.
