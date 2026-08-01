<div align="center">

# NotepadX

**The Windows 11 Notepad, on Windows 10 too — without the AI.**

Tabs, dark mode, session restore, a real find-and-replace bar.
No Copilot, no Rewrite, no Summarize, no account, no telemetry, no network code at all.

[![CI](https://github.com/OWNER/REPO/actions/workflows/ci.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/ci.yml)
[![Release](https://github.com/OWNER/REPO/actions/workflows/release.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/release.yml)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

</div>

---

## Why

Windows 11 made Notepad genuinely better — tabs, a proper dark theme, text that survives a
reboot. Then it filled the same window with AI features, and left every one of those
improvements out of Windows 10.

NotepadX keeps the good half. It is a single executable, it starts instantly, it never
touches the network, and it runs the same on Windows 10 and Windows 11.

---

## Install

Download the latest build from [Releases](../../releases) and run it. No installer, no
.NET runtime, no admin rights.

| Download | For |
| --- | --- |
| `NotepadX-vX.Y-win-x64.exe` | Windows 10 (1809 or newer) and Windows 11, 64-bit |
| `NotepadX-vX.Y-win-arm64.exe` | Windows on ARM |
| `NotepadX-vX.Y-win-x64-portable.zip` | Same app, keeps its settings beside the executable |

Windows SmartScreen will warn on first run, as it does for anything not signed by a paid
certificate authority. Builds are Authenticode-signed and timestamped, so you can confirm
what you downloaded:

```powershell
Get-AuthenticodeSignature .\NotepadX.exe | Format-List Status, SignerCertificate
Get-FileHash .\NotepadX.exe -Algorithm SHA256   # compare against SHA256SUMS.txt
```

### Making it the default text editor

Right-click any `.txt` file → **Open with** → **Choose another app** → browse to
`NotepadX.exe` → tick **Always use this app**.

---

## Features

### Tabs
New (`Ctrl+N`), close (`Ctrl+W`), middle-click to close, drag to reorder, cycle with
`Ctrl+Tab`. Right-click a tab for close others, close to the right, rename the file on
disk, copy full path, or open the containing folder. Each tab keeps its own undo history,
caret position and scroll offset across switches.

### Nothing gets lost
Unsaved text is written to a local recovery buffer as you type and comes back after a
close, a crash or a reboot — along with tab order, caret positions and window geometry.
Switch it off under **Settings → Files → When the app starts**.

### Find and replace
An inline bar, not a modal dialog that covers your text. Live match count, match case,
whole word, wrap around, find previous/next, replace, replace all.

### Encodings and line endings
Detected on open — UTF-8, UTF-8 with BOM, UTF-16 LE/BE, ANSI, and CRLF / LF / CR — shown
in the status bar and changed by clicking them. The encoding menu also offers **Reopen
with encoding** for files that were guessed wrong.

### Everything else
Light / dark / follow-Windows theming that also recolours the window frame and picks up
your Windows accent colour; word wrap; zoom (`Ctrl` `+`/`-`/`0` or `Ctrl`+scroll); status
bar with line, column and character count; go to line; time and date stamp; printing;
font family, size and style; optional offline spell check; and drag-and-drop of files onto
the window.

Opening a file from Explorer adds a tab to the running window, the way Windows 11 Notepad
does. **Settings → Files** switches that to a new window instead.

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
| `Ctrl+Shift+W` | Close window | `Ctrl+Tab` | Next tab |

---

## Where your data lives

```
%LOCALAPPDATA%\NotepadX\
├── settings.json    preferences
├── session.json     open tabs and window geometry
└── buffers\         unsaved text, one file per tab, deleted the moment you save
```

**Settings → About → Open data folder** takes you there. Deleting the folder resets the
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

That is the whole list. No workloads, no NuGet packages, no Visual Studio required —
though Visual Studio 2022 17.14+, Rider, or VS Code with the C# Dev Kit all work.

### Clone and run

```powershell
git clone https://github.com/OWNER/REPO.git
cd REPO
dotnet run
```

### Produce a release binary

```powershell
.\build.ps1                       # dist\x64\NotepadX.exe, self-contained, ~59 MB
.\build.ps1 -Arch arm64           # Windows on ARM
.\build.ps1 -Framework            # ~2 MB, requires the .NET 10 Desktop Runtime installed
.\build.ps1 -Portable             # adds the NotepadX.portable marker
.\build.ps1 -Sign -Version 1.4    # signed and version-stamped, as CI does it
```

The app icon is generated rather than committed. `build.ps1` runs `tools\make-icon.ps1`
automatically when `Assets\app.ico` is missing.

---

## Contributing

Pull requests are welcome. The project is small and deliberately dependency-free — please
keep it that way unless there is a strong reason.

### Getting set up on a new machine

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and
   confirm it: `dotnet --version` should print `10.x`.
2. Fork and clone the repository.
3. `dotnet build` — a clean build must produce zero warnings; CI runs with
   `-warnaserror`.
4. `dotnet run` to launch it.
5. Work on a branch. Pushing to a branch other than `main` runs the CI build and uploads
   the resulting executable as an artifact, so you can test a build without a release.

You do **not** need the signing key to contribute. The release workflow skips signing when
the secrets are absent, so forks build and release fine without it.

### Project layout

```
App.xaml.cs              startup, single instance, session bootstrap
MainWindow.xaml(.cs)     title bar, tabs, menus, find bar, status bar, commands
Models/DocumentTab       one tab; owns its TextBox so undo history survives tab switches
Services/AppSettings     preferences with change notification
Services/SessionStore    session.json plus per-tab recovery buffers
Services/TextFileIo      encoding and line-ending detection, atomic save
Services/ThemeManager    Fluent theme + palette swapping, Windows accent, theme watch
Services/SingleInstance  named-pipe handoff so a second launch becomes a tab
Interop/NativeMethods    DWM and window-message plumbing
Themes/                  Light, Dark and shared control styles
tools/                   icon generator, signing-key generator
```

### Two traps to know before editing `MainWindow`

- `BuildCommands()` runs **before** `InitializeComponent()`. The commands are plain
  properties with no change notification, so any `Command="{Binding …}"` evaluated first
  binds to `null` and stays that way — menus and buttons silently stop working.
- Shortcuts are matched in `OnPreviewKeyDown`, not left to the bubbling `InputBindings`.
  `TextBox` consumes several of them first; `Ctrl+H` is backspace in a Win32 edit control.

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

Signing is optional and automatic. Add the `SIGNING_CERT_BASE64` and
`SIGNING_CERT_PASSWORD` repository secrets and every release gets signed; leave them out
and releases are published unsigned. See [SIGNING-KEY.md](SIGNING-KEY.md).

---

## License

MIT — see [LICENSE](LICENSE).
