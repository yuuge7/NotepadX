<div align="center">

# NotepadX

**The Windows 11 Notepad, on Windows 10 too — without the AI.**

Tabs, dark mode, session restore, regex find and replace, line numbers.
No Copilot, no Rewrite, no Summarize, no account, no telemetry, no network code at all.

[![CI](https://github.com/yuuge7/NotepadX/actions/workflows/ci.yml/badge.svg)](https://github.com/yuuge7/NotepadX/actions/workflows/ci.yml)
[![Release](https://github.com/yuuge7/NotepadX/actions/workflows/release.yml/badge.svg)](https://github.com/yuuge7/NotepadX/actions/workflows/release.yml)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![License MIT](https://img.shields.io/badge/license-MIT-green)

</div>

---

## Why

Windows 11 made Notepad genuinely better — tabs, a proper dark theme, text that survives a
reboot. Then it filled the same window with AI features, and left every one of those
improvements out of Windows 10.

NotepadX keeps the good half and adds the things a plain-text editor should have had all
along. It is a single executable, it starts instantly, it never touches the network, and
it runs the same on Windows 10 and Windows 11.

---

## Install

Download from [Releases](../../releases). No .NET runtime and no administrator rights are
needed for any of these.

| Download | For |
| --- | --- |
| `NotepadX-vX.Y-win-x64.exe` | Windows 10 (1809 or newer) and Windows 11, 64-bit. Just run it. |
| `NotepadX-vX.Y-win-arm64.exe` | Windows on ARM. Just run it. |
| `NotepadX-vX.Y-win-x64.msi` | Per-user installer: Start menu entry, Open with, clean uninstall |
| `NotepadX-vX.Y-win-x64-portable.zip` | Same app, keeps its settings beside the executable |

Windows SmartScreen will warn on first run, as it does for anything not signed by a paid
certificate authority. Builds are Authenticode-signed and timestamped, so you can confirm
what you downloaded:

```powershell
Get-AuthenticodeSignature .\NotepadX.exe | Format-List Status, SignerCertificate
Get-FileHash .\NotepadX.exe -Algorithm SHA256   # compare against SHA256SUMS.txt
```

### Making it the default text editor

Open **Settings › Default app › Register file types**, then **Open Windows default apps**
and pick NotepadX for the types you want. Windows has not allowed an application to claim
a file type on its own since Windows 8 — the final choice is always yours to confirm.

---

## Features

### Tabs
New (`Ctrl+N`), close (`Ctrl+W`), reopen a closed one (`Ctrl+Shift+T`), middle-click to
close, drag to reorder, cycle with `Ctrl+Tab`. Right-click a tab for close others, close
to the right, rename the file on disk, copy full path, or open the containing folder.
Each tab keeps its own undo history, caret position and scroll offset across switches.

### Nothing gets lost
Unsaved text is written to a local recovery buffer as you type and comes back after a
close, a crash or a reboot — along with tab order, caret positions and window geometry.

If another program changes a file you have open, NotepadX notices when the window regains
focus: a clean tab reloads itself, a modified one asks first. Saving over a file that
changed underneath you asks as well.

### Find and replace
An inline bar, not a modal dialog that covers your text. Live match count, every match in
view highlighted, match case, whole word, wrap around, recent-search history, and **full
regular expressions** with `$1` group references in the replacement.

### Text tools
Under **Edit › Text tools**, applied to the selection or the whole document: sort lines,
reverse line order, remove duplicate or empty lines, trim trailing whitespace, join lines,
UPPERCASE / lowercase / Title Case / invert case, insert the file path.

### Encodings and line endings
Detected on open — UTF-8, UTF-8 with BOM, UTF-16 LE/BE, ANSI, and CRLF / LF / CR — shown
in the status bar and changed by clicking them. The encoding menu also offers **Reopen
with encoding** for files that were guessed wrong.

Saving text that a legacy code page cannot represent stops and tells you exactly how many
characters would be destroyed, instead of silently writing `?`.

### Everything else
Light / dark / follow-Windows theming that recolours the window frame and picks up your
Windows accent colour; line numbers; word wrap; zoom (`Ctrl` `+`/`-`/`0` or `Ctrl`+scroll);
status bar with line, column, character and optional word counts; go to line; time and
date stamp; printing; font family, size and style; optional offline spell check;
read-only file handling; a warning before opening a file large enough to make editing
slow; and drag-and-drop of files onto the window.

Opening a file from Explorer adds a tab to the running window, the way Windows 11 Notepad
does. **Settings › Files** switches that to a new window instead.

### Keyboard

| | | | |
| --- | --- | --- | --- |
| `Ctrl+N` | New tab | `Ctrl+F` | Find |
| `Ctrl+Shift+N` | New window | `Ctrl+H` | Replace |
| `Ctrl+O` | Open | `F3` / `Shift+F3` | Find next / previous |
| `Ctrl+S` | Save | `Ctrl+G` | Go to line |
| `Ctrl+Shift+S` | Save as | `Ctrl+P` | Print |
| `Ctrl+Alt+S` | Save all | `F5` | Insert time and date |
| `Ctrl+W` | Close tab | `Ctrl` `+` / `-` / `0` | Zoom in / out / reset |
| `Ctrl+Shift+T` | Reopen closed tab | `Ctrl+Tab` | Next tab |
| `Ctrl+Shift+W` | Close window | `Esc` | Close the find bar |

### Command line

```
NotepadX [options] [file ...]

  file              Open the file
  file:42           Open the file and jump to line 42

  /p, --print       Print each file to the default printer, then exit
  -n, --new-window  Force a new window instead of a tab in the running one
  -h, --help, /?    Show usage
```

---

## Where your data lives

```
%LOCALAPPDATA%\NotepadX\
├── settings.json    preferences
├── session.json     open tabs and window geometry
└── buffers\         unsaved text, one file per tab, deleted the moment you save
```

**Settings › About › Open data folder** takes you there. Deleting the folder resets the
app completely. Nothing is written anywhere else, and nothing leaves the machine.

Put an empty file named `NotepadX.portable` next to the executable and all of the above
moves to a `Data` folder beside it instead — useful on a USB stick. The portable release
zip already contains that marker.

Saving writes to a temporary file in the destination folder and then swaps it in, so an
interrupted save can never leave a half-written file behind.

---

## Building from source

### Requirements

- Windows 10 1809 or newer
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

That is the whole list for the app itself. No workloads and no NuGet packages — the only
dependencies in the repository are the test framework and, for the installer, the WiX
toolset, which `packaging\build-msi.ps1` installs as a local tool on first run.

### Clone and run

```powershell
git clone https://github.com/yuuge7/NotepadX.git
cd NotepadX
dotnet run
```

### Build and test

```powershell
dotnet build NotepadX.slnx -c Release -warnaserror
dotnet test tests\NotepadX.Tests\NotepadX.Tests.csproj
```

### Produce release artifacts

```powershell
.\build.ps1                              # dist\x64\NotepadX.exe, self-contained
.\build.ps1 -Arch arm64                  # Windows on ARM
.\build.ps1 -Framework                   # 0.4 MB, needs the .NET 10 Desktop Runtime
.\build.ps1 -Portable                    # adds the NotepadX.portable marker
.\build.ps1 -Sign -Version 1.4           # signed and version-stamped, as CI does it

.\packaging\build-msi.ps1 -Version 1.4   # the per-user MSI installer
.\packaging\make-winget-manifests.ps1 -Version 1.4
.\packaging\measure-startup.ps1          # size vs startup, measured rather than guessed
```

The app icon is generated rather than committed; `build.ps1` runs `tools\make-icon.ps1`
when `Assets\app.ico` is missing.

### On the 59 MB download

WPF supports neither trimming nor NativeAOT, so single-file compression is the only size
lever that exists. Measured on a Windows 10 desktop, six warm runs each:

| Build | Size | First run | Warm start (median) |
| --- | --- | --- | --- |
| Self-contained, compressed | 58.7 MB | 3.6 s | 1.89 s |
| Self-contained, uncompressed | 124.8 MB | 3.6 s | 1.66 s |
| Framework-dependent | 0.4 MB | — | needs the runtime installed |

Compression costs about 230 ms of warm start and saves 66 MB of download, so releases ship
compressed. Rerun `packaging\measure-startup.ps1` on your own hardware before changing that.

---

## Contributing

Pull requests are welcome. The project is small and deliberately dependency-free — please
keep it that way unless there is a strong reason.

### Getting set up on a new machine

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and
   confirm it: `dotnet --version` should print `10.x`.
2. Fork and clone the repository.
3. `dotnet build NotepadX.slnx` — a clean build must produce zero warnings; CI runs with
   `-warnaserror`.
4. `dotnet test tests\NotepadX.Tests\NotepadX.Tests.csproj` — should report 89 passing.
5. `dotnet run` to launch it.
6. Work on a branch. Pushing to a branch other than `main` runs the CI build and uploads
   the resulting executable as an artifact, so you can test a build without a release.

You do **not** need the signing key to contribute. The release workflow skips signing when
the secrets are absent, so forks build and release fine without it.

### Project layout

```
App.xaml.cs              startup, single instance, command line, session bootstrap
MainWindow.xaml(.cs)     title bar, tabs, menus, find bar, status bar, commands
Models/DocumentTab       one tab; owns its TextBox so undo history survives tab switches
Controls/                line number gutter, search match highlighter
Services/AppSettings     preferences with change notification
Services/SessionStore    session.json plus per-tab recovery buffers
Services/TextFileIo      encoding and line-ending detection, atomic save, encodability probe
Services/DocumentSearch  find and replace, plain and regex
Services/TextTools       line and case transforms
Services/CommandLine     argument parsing, including the file:line form
Services/FileAssociation shell registration for Open with and Default apps
Services/ThemeManager    Fluent theme + palette swapping, Windows accent, theme watch
Services/SingleInstance  named-pipe handoff so a second launch becomes a tab
Interop/NativeMethods    DWM and window-message plumbing
Themes/                  Light, Dark and shared control styles
tests/                   xUnit tests for everything that is not UI
packaging/               MSI, winget manifests, startup measurement
tools/                   icon generator, signing-key generator
```

The rule of thumb: anything that can be written without a `Window` reference belongs in
`Services/` with tests. `MainWindow` should only wire things together.

### Traps worth knowing before editing `MainWindow`

- `BuildCommands()` runs **before** `InitializeComponent()`. The commands are plain
  properties with no change notification, so any `Command="{Binding …}"` evaluated first
  binds to `null` and stays that way — menus and buttons silently stop working.
- Shortcuts are matched in `OnPreviewKeyDown`, not left to the bubbling `InputBindings`.
  `TextBox` consumes several of them first; `Ctrl+H` is backspace in a Win32 edit control.
- Anything that measures the editor — the line gutter, the match highlighter — must run
  after layout. Called straight from `TextChanged`, `GetFirstVisibleLineIndex` reports
  nothing and the paint is silently lost.
- `NotepadX.csproj` sits at the repository root, so its default globs would swallow
  `tests/` and `packaging/`. They are excluded explicitly; add new top-level folders to
  that exclusion list.

### How Windows 10 support is kept working

This is easy to break by accident, so it is worth stating explicitly:

- Icons come from **Segoe MDL2 Assets**, which exists on Windows 10. Segoe Fluent Icons is
  Windows 11 only and is never referenced. UI text asks for
  `Segoe UI Variable Text, Segoe UI` so Windows 10 falls back cleanly.
- Mica and rounded corners go through `DwmSetWindowAttribute` and are silently skipped on
  builds that predate them. The dark window frame falls back to the Windows 10 1809
  attribute id when the newer one is rejected.
- The project targets the default `net10.0-windows` platform floor. Every OS-version
  dependent call is guarded by `OperatingSystem.IsWindowsVersionAtLeast` at runtime.
- Maximizing is constrained to the monitor work area through `WM_GETMINMAXINFO`, so the
  borderless window does not slide under the taskbar.

If you add a Windows 11 era API, guard it and provide a fallback.

---

## Releases

Every push to `main` publishes a release automatically. The version is taken from the
newest `vX.Y` tag with the minor number incremented, so the first run produces
**NotepadX v1.0**, then **v1.1**, and so on.

- Skip a release for a given push: put `[skip release]` in the commit message.
- Start a new major version: `git tag v2.0 && git push origin v2.0`, then push to `main`.
- Publish a specific version by hand: run the **Release** workflow from the Actions tab
  and enter a version.

Each release runs the tests, builds x64 and arm64 executables plus the MSI, signs
everything if the secrets are present, generates winget manifests, and attaches
`SHA256SUMS.txt`.

Signing is optional and automatic. Add the `SIGNING_CERT_BASE64` and
`SIGNING_CERT_PASSWORD` repository secrets and every release gets signed; leave them out
and releases are published unsigned. See [SIGNING-KEY.md](SIGNING-KEY.md).

### Publishing to winget

The release attaches `NotepadX-vX.Y-winget-manifests.zip`. To list the package:

1. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
2. Copy the three manifest files to
   `manifests\y\yuuge7\NotepadX\<version>\`.
3. `winget validate --manifest <folder>` then
   `winget install --manifest <folder>` to check it locally.
4. Open a pull request against winget-pkgs.

After the first submission is merged, subsequent versions can be automated with
`wingetcreate update`.

---

## License

MIT — see [LICENSE](LICENSE).
