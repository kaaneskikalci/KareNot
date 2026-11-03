# Changelog

All notable changes to KareNot are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions are the Git tags (`v1.4`, …) that the release workflow builds.

## [Unreleased]

## [1.5] - 2026-11-03

### Fixed
- Times after the first minute were shown and written one tenth of a second too early in about 4% of cases
  (e.g. 60.3 s as `00:01:00.2`) because of floating-point rounding; `NaN` printed as `00:00:NaN`.
- Bug clips (`C`) for notes in the first seconds of a video ran past the chosen length (a note at 0:02 gave 0:00–0:10
  instead of 0:00–0:07).
- A second findings package (ZIP) made in the same minute failed and deleted the first one. The new one is now named
  `… (2).zip`.
- A cancelled or failed "bleeped and cut video" export deleted the previous export. The new video now replaces the old
  one only when it is complete.
- An `inceleme.json` that could not be read (locked by another program, an offline cloud placeholder, a broken edit) was
  replaced by an empty file within 5 seconds, losing the resume position, review progress, build and tester of every
  other video. A corrupt file is now kept as `inceleme.json.bozuk-<time>`.
- Lines of `notlar.txt` edited by hand that could not be read (seconds written as `65,4`, a lost tab) were silently
  dropped on the next save. The seconds now also accept a decimal comma or fall back to the time column, and lines that
  still can't be read are copied to `notlar_okunamayan.txt`.
- A `null` value in `ayarlar.json` (e.g. `"DataFolder": null`) crashed the app on every start; negative or zero lengths
  in the settings are replaced by the defaults.
- `[` and `]` (previous / next mark) did nothing on a Turkish Q keyboard, where they are typed with AltGr.

### Added
- xUnit test project (`tests/KareNot.Tests`) for the note file format, time formatting, folder naming, cut and clip
  range math, all export formats, settings, `inceleme.json` and whisper output parsing.
- GitHub Actions: build and test on Windows for every push and pull request (plus the IINA format tests on node), and a
  release workflow that builds `KareNot-<tag>-win-x64.zip` when a `v*` tag is pushed.
- English README, Turkish guide in `README.tr.md`, `CONTRIBUTING.md` and this changelog.
- MIT license (`LICENSE`).

### Changed
- Releases built by the workflow ship a ZIP (`KareNot.exe`, README, CHANGELOG) with a SHA-256 checksum. The bare
  `KareNot.exe` is still attached, so `releases/latest/download/KareNot.exe` links keep working.

### IINA plugin (experimental, unverified on a Mac)
- Fixed from a static review against IINA's source; only smoke-tested against a fake IINA API, never in IINA:
  - the sidebar and overlay message handlers were registered before `loadFile`, which clears them, so the panel did
    nothing; they are now registered right after it;
  - `file.delete` and overwriting `file.write` only work under `@tmp` / `@data`, so deleting a video's last note,
    removing stale `.tmp` files and releasing the migration lock failed; these now use `/bin/rm` and a fresh `.tmp`
    moved into place with `/bin/mv`;
  - a backtick or `${` in a note or video name broke the sidebar state message;
  - video names from macOS (NFD) did not match notes written on Windows (NFC);
  - a note could be saved under the next video if the file changed while its screenshot was taken;
  - `fmt` had the same tenth-of-a-second rounding bug as the Windows app.
- Added *Not klasörünü seç…* / *Not klasörünü varsayılana döndür* to choose the notes folder (default stays
  `~/Desktop/KareNot`, so a synced Windows `KareNot Notlar` folder can be shared).

### Known issues
- The IINA plugin (`iina/`) has never been run on a Mac. Single-letter key bindings may lose to mpv's own bindings, and
  the open points in `iina/README.md` are still unverified.

## [1.4] - 2026-10-28
### Changed
- Notes are kept in `KareNot Notlar` next to `KareNot.exe` (`Desktop\KareNot` when that folder is not writable; an
  existing v1.3 `Desktop\KareNot` stays in use). The folder can be chosen in the settings, with an offer to move the
  existing notes.
### Added
- "Kayıt dosyası" footer button opens the video's `notlar.txt`; edits are read back when KareNot is activated.
### Fixed
- Seeking after the end of a video occasionally stayed at 0.

## [1.3] - 2026-10-28
### Changed
- One folder per video (`<video name>\notlar.txt`, `ss\`, report, `cikti\`); the combined `video_notlari.txt` is no
  longer written. Notes of the old `Desktop\Video Notlari` layout are moved once, nothing is deleted.
### Fixed
- Seeking after the end of a video sometimes stayed at 0.

## [1.2] - 2026-10-28
### Added
- New dark UI; status and severity for game notes, search and filters, note templates, bug clips, undo / redo.
- Editing marks as ranges, bleep ranges by holding `K`, watching with cuts skipped, profanity candidates (whisper.cpp)
  and dead-air candidates (ffmpeg).
- Resume, "reviewed up to here", the Videolar tab, jumping between marks, looping, seek bar frame preview.
- Exports: HTML + Markdown report, task list, findings ZIP, editing notes JSON, DaVinci Resolve EDL, edited video, CSV.
- IINA plugin for macOS (untested).

## [1.1] - 2026-10-28
### Changed
- Renamed from "Video Notları" to KareNot, new logo; side panel split into game test, editing and shortcuts tabs.

## [1.0] - 2026-10-28
- First release ("Video Notları"): timestamped notes on video recordings with screenshots.

[Unreleased]: https://github.com/kaaneskikalci/KareNot/compare/v1.5...HEAD
[1.5]: https://github.com/kaaneskikalci/KareNot/compare/v1.4...v1.5
[1.4]: https://github.com/kaaneskikalci/KareNot/compare/v1.3...v1.4
[1.3]: https://github.com/kaaneskikalci/KareNot/compare/v1.2...v1.3
[1.2]: https://github.com/kaaneskikalci/KareNot/compare/v1.1...v1.2
[1.1]: https://github.com/kaaneskikalci/KareNot/compare/v1.0...v1.1
[1.0]: https://github.com/kaaneskikalci/KareNot/releases/tag/v1.0
