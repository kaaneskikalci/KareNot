# Contributing to KareNot

Thanks for helping. KareNot is a small Windows Forms app; this page covers building, testing and releasing it.

## Repository layout

| Path | What |
|---|---|
| `*.cs`, `KareNot.csproj` | The Windows app (.NET 10, Windows Forms, libvlc 3 through P/Invoke) |
| `MainForm*.cs` | The main window, split by feature: review, export, analysis, self test |
| `Notes.cs`, `Settings.cs`, `Reports.cs`, `Ffmpeg.cs`, `Whisper.cs` | File format, storage, exports and tool wrappers — the testable logic |
| `tests/KareNot.Tests/` | xUnit tests for that logic |
| `iina/` | IINA plugin for macOS (untested on a Mac) and its node format tests |
| `.github/workflows/` | CI (`ci.yml`) and releases (`release.yml`) |

## Prerequisites

- Windows 10/11 x64 with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [VLC media player 64-bit](https://www.videolan.org/vlc/) to run the app (not needed for the unit tests)
- Optional: ffmpeg on `PATH`, whisper.cpp; Node.js 18+ for the IINA format tests

## Build and run

```bash
dotnet build KareNot.csproj
```

```bash
dotnet run --project KareNot.csproj -- "C:\path\to\recording.mp4"
```

Release build (framework-dependent single file in `yayin\KareNot.exe`):

```bash
dotnet publish KareNot.csproj -c Release -o yayin
```

The repository root holds only `KareNot.csproj`, so plain `dotnet build` in the root works too. There is no solution file
on purpose: a second project or solution file in the root would make `dotnet build` / `dotnet publish` ambiguous.

On Linux or macOS the app compiles with `-p:EnableWindowsTargeting=true`, but neither the app nor the tests can run there.

## Tests

```bash
dotnet test tests/KareNot.Tests
```

- Keep logic that can be tested free of UI and player calls (see `Ffmpeg.ClipRange`, `NoteStore.ParseSeconds`,
  `AppSettings.Parse`) and add a test with every fix; a regression test should fail before the fix.
- Tests that touch files use `TempData` (`tests/KareNot.Tests/TestEnvironment.cs`), which points the data folder at a
  temporary directory through the `VIDEONOTLARI_DATA` environment variable. Tests run one at a time for that reason.
- The note file format is shared with the IINA plugin. When it changes, update `iina/KareNot.iinaplugin/lib/format.js`
  as well and run `node iina/test/format_test.js`.

The end-to-end self test drives the real window, player and mouse (keep your hands off the mouse while it runs):

```bash
set VIDEONOTLARI_DATA=%TEMP%\karenot_selftest
KareNot.exe --selftest selftest.log C:\path\to\folder\video1.mp4
```

The video's folder must contain a second video. Results are written to the log file.

## Commits and pull requests

- One logical change per commit, with a [Conventional Commits](https://www.conventionalcommits.org/) prefix as in the
  history: `fix:`, `feat:`, `test:`, `docs:`, `ci:`, `refactor:`. Say in the body why the change is needed and how a
  bug was found.
- CI must be green: the Windows build and tests and the IINA format tests run on every push and pull request.
- Add user-visible changes to the `[Unreleased]` section of `CHANGELOG.md`.
- The UI is in Turkish; keep new UI text in Turkish and code comments in English.

## Releasing

1. Move the `[Unreleased]` entries in `CHANGELOG.md` under a new version heading and merge to `main`.
2. Tag and push:

   ```bash
   git tag v1.5
   ```

   ```bash
   git push origin v1.5
   ```

3. `release.yml` runs the tests, publishes with that version, and attaches three files to the GitHub release (creating
   it with generated notes, or adding the files to a release you already wrote for that tag):
   - `KareNot-v1.5-win-x64.zip` (`KareNot.exe`, README, CHANGELOG) and its `.sha256`;
   - the bare `KareNot.exe`, so links to `releases/latest/download/KareNot.exe` from v1.0–v1.4 keep working.

   Edit the release notes afterwards if needed.
