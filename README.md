# Pet Player

A lightweight, translucent, keyboard-first Windows video player built with **C#, .NET 8, WPF and LibVLCSharp**. Designed to run on a locked-down, company-managed Windows device with no administrator access, no system-wide VLC install, and no installer.

```
open video → watch → seek precisely → synchronize subtitles → optionally inspect transcript → close
```

## Features

**Playback**
- Plays common video/audio containers via LibVLC (MP4, MKV, AVI, MOV, WebM, and more)
- Play/Pause, seek forward/backward (configurable interval, default 5s), frame-precise nudging (`Ctrl` + `←`/`→`), medium seeks (`Shift` + `←`/`→`), 10-second jumps (`J`/`L`)
- Adjustable playback speed (0.25x–4x), with quick increase/decrease and a one-key reset to 1.0x
- Volume control via the on-screen slider, keyboard (`↑`/`↓`), or the mouse scroll wheel anywhere over the video; mute toggle
- Drag a video or subtitle file onto the window to open or attach it; or pass a file path as a command-line argument / via Explorer "Open with"
- Optional resume-from-last-position, capped to a bounded history

**Window & video display**
- Borderless, translucent window with a custom title bar (shows the currently playing file name) and an auto-hiding bottom control bar - both appear on mouse movement and hide again after a few seconds of inactivity during playback
- True fullscreen (`F` or `Enter`) that covers the entire screen, taskbar included; separately, Maximize/Restore behaves like an ordinary window and correctly leaves the taskbar visible
- The window can be resized by dragging any edge, even though the video surface visually covers the entire client area
- Any padding around the video (when its aspect ratio doesn't match the window's) stays solid black at all times, including while interactively resizing
- Always-on-top toggle

**Subtitles**
- Automatically loads a same-named subtitle file next to the video (can be turned off in Settings); external subtitle files can also be attached manually
- Show/hide toggle, track selection, sync offset (±100 ms / ±500 ms, with reset), and vertical position nudging - all from the right-click menu
- Renders as plain text with an outline for readability, with no background box behind it
- Font size, vertical position, text opacity, and outline are configurable in Settings (take effect the next time Pet Player is started - see [Known v1 scope notes](#known-v1-scope-notes))

**Audio & transcript**
- Audio track selection, correctly reflecting whichever track is actually playing
- Optional transcript panel, synced to subtitle timing, toggled from the control bar or right-click menu

**Right-click context menu**
- Play/Pause, seek backward/forward, playback speed, audio track, subtitles (with all sync/position actions), fullscreen, always-on-top, show transcript, open file / open file location / copy file path, media information, and settings - styled to match the native Windows context-menu look

**Settings window**
- Tabbed layout (General, Playback, Subtitles, Transcript, Windows, Shortcuts, About) covering seek interval, volume step, autoplay, resume playback, controls-hide delay, subtitle appearance, whether the transcript panel is visible by default, Windows "Open With" registration, and a full reference of every keyboard/mouse shortcut

**Windows integration**
- Optional, fully reversible registration in Explorer's right-click "Open with" menu - writes only to the per-user registry hive, no admin rights needed
- Single-instance: opening another file (e.g. via "Open with", or a second launch) hands the path to the already-running window instead of opening a second copy
- Runs with no installer and no admin rights required, straight from a plain folder copy

See [Keyboard shortcuts](#keyboard-shortcuts) below for the full list of key bindings.

## Requirements

**To build:**
- **Windows 10/11.** This project cannot be built or run on macOS/Linux - LibVLCSharp.WPF and the
  bundled LibVLC runtime are Windows-only.
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** - the SDK, not just the
  runtime (`dotnet --version` should print something starting with `8.`).
- **PowerShell** to run `build\Build.ps1` - already included with Windows, nothing extra to
  install.
- No Visual Studio required: the `dotnet` CLI (used by `Build.ps1`) is all you need. If you'd
  rather use an IDE, opening `PetPlayer.sln` in Visual Studio 2022 (17.8+), VS Code with the C#
  extension, or JetBrains Rider all work too.

**To run the built app:** nothing extra, if you use the recommended self-contained build (see
[Deployment](#deployment)). No admin rights, no VLC install, no .NET runtime install on the
machine you run it on.

## Quick start (first build after cloning)

```powershell
git clone <this repository's URL>
cd PetPlayer
.\build\Build.ps1
```

Then run `dist\PetPlayer\PetPlayer.exe`. There's nothing to configure beforehand - `Build.ps1`
restores NuGet packages, builds, and publishes in that one step.

If PowerShell refuses to run the script (a default Windows security setting that blocks
unsigned `.ps1` files from anywhere, not something specific to this project), run it as:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\Build.ps1
```

See [Building & rebuilding after code changes](#building--rebuilding-after-code-changes) below
for what to run every time you edit the code after that first build.

## Project layout

```
PetPlayer/
├── PetPlayer.sln
├── PetPlayer/
│   ├── PetPlayer.csproj
│   ├── App.xaml / App.xaml.cs        Startup, single-instance handoff, global error handling
│   ├── app.manifest                  asInvoker (no UAC), per-monitor DPI awareness
│   ├── Views/                        MainWindow, SettingsWindow, TranscriptPanelView
│   ├── ViewModels/                   MainViewModel, SettingsViewModel
│   ├── Services/                     MediaPlayerService, SubtitleService, TranscriptService,
│   │                                 SettingsService, WindowsIntegrationService,
│   │                                 SingleInstanceService, LoggingService, ITranscriptionService
│   ├── Models/                       AppSettings, SubtitleTrack, AudioTrack, TranscriptEntry
│   ├── Helpers/                      PlaybackConstants, TimeFormatter, FileTypeHelper, PathHelper,
│   │                                 ValueConverters
│   └── Resources/                    PetPlayer.ico
├── assets/                           Source PetPlayerIcon.png (used to generate the .ico)
└── build/
    └── Build.ps1                     Restore, build, publish, verify, stage dist\PetPlayer
```

`TranscriptPanelView` intentionally has no dedicated view model - transcript state (active line,
entries) is inseparable from playback position, which already lives in `MainViewModel`. Giving it
its own view model would just duplicate that state.

## Building & rebuilding after code changes

(See [Requirements](#requirements) for what needs to be installed first, and
[Quick start](#quick-start-first-build-after-cloning) if this is your very first build.)

**Every time you change code and want a new, runnable `.exe`:**

```powershell
.\build\Build.ps1
```

This is the one command to remember. It restores NuGet packages, builds in Release configuration,
publishes a self-contained `win-x64` build, verifies that `libvlc.dll`, `libvlccore.dll`, the
`plugins\` folder and the icon all made it into the output, and stages the finished app at
`dist\PetPlayer\`. Run `dist\PetPlayer\PetPlayer.exe` to try your changes - that folder is exactly
what you'd copy anywhere else to deploy it.

> **If Pet Player is currently running, close it first.** Windows keeps `PetPlayer.exe` locked while
> it's open, so publishing over it will fail until you do.

For a faster sanity check while iterating on code - just confirming it compiles, without producing
a distributable build:

```powershell
dotnet build PetPlayer.sln -c Debug
```

This is quicker but produces `PetPlayer\bin\Debug\net8.0-windows\PetPlayer.exe`, which is **not**
suitable for real testing or handing to anyone else - it depends on files that are only present in
the build output during development, not a standalone folder. Always use `.\build\Build.ps1` (or
`dotnet publish` directly, which it wraps) to produce the real, self-contained app under `dist\`.

`PetPlayer\bin\` and `PetPlayer\obj\` are build intermediates, safe to delete at any time (they're
already excluded from version control via `.gitignore`) - the next build regenerates them from
scratch. `dist\` is the actual shipped app and is *not* regenerated automatically; only a fresh
`.\build\Build.ps1` run updates it.

## Deployment

Pet Player needs **no installer**. Copy the published folder anywhere (a network share, a USB
drive, `%LOCALAPPDATA%\Programs`, ...) and run `PetPlayer.exe`.

Two publish modes are available:

| | Self-contained (default) | Framework-dependent |
|---|---|---|
| Target machine needs | Nothing | .NET 8 Desktop Runtime already installed |
| Output size | ~356 MB | ~197 MB |
| Command | `.\build\Build.ps1` | `.\build\Build.ps1 -FrameworkDependent` |

(Most of that size in both cases is the bundled LibVLC runtime and its decoder/demuxer plugins,
not .NET itself - LibVLC is what makes the format-support requirement possible without a system
VLC install, so it dominates either way.)

**Recommendation for a managed company device: self-contained.** The whole point of this
deployment is that IT cannot be asked to pre-install anything and the end user has no admin
rights - so relying on the .NET Desktop Runtime already being present is a gamble, and installing
it later would itself typically require admin rights. Self-contained is larger, but it is
guaranteed to run from a plain folder copy with zero prerequisites, which is exactly the
"no installer required" requirement.

`Build.ps1` restores, builds, publishes (self-contained `win-x64` by default), verifies that
`libvlc.dll`, `libvlccore.dll`, the `plugins\` folder and the icon all made it into the output, and
stages the ready-to-run folder at `dist\PetPlayer\`.

The LibVLC runtime is bundled via the `VideoLAN.LibVLC.Windows` NuGet package, which drops it into
`libvlc\win-x64\` next to the executable at both build and publish time. `MediaPlayerService`
resolves that path explicitly from the running executable's own directory
(`AppContext.BaseDirectory`) - Pet Player never depends on `C:\Program Files\VideoLAN\VLC` or any
other system install.

## Windows "Open With" integration

Open **Settings → Windows** inside Pet Player and click **Register Pet Player in "Open
with"**. This writes only to `HKEY_CURRENT_USER\Software\Classes\Applications\PetPlayer.exe` - no
`HKEY_LOCAL_MACHINE`, no admin prompt, no default-app takeover. After registering, right-click any
supported video file in Explorer → **Open with** → **Pet Player** will be available. Use **Remove
from "Open with"** to cleanly delete only Pet Player's own registry keys.

If your device's security policy blocks per-user registry writes, Pet Player shows a plain message
explaining that and continues to work normally otherwise - it never crashes because of a blocked
registration.

You can also just launch it directly:

```powershell
PetPlayer.exe "C:\Videos\Example Video.mkv"
```

Pet Player is single-instance: launching it again with a different file (e.g. via "Open with")
hands the new file path to the already-running instance over a named pipe and brings it to the
foreground, instead of opening a second window.

## Keyboard shortcuts

| Key | Action |
|---|---|
| Space | Play / Pause |
| ← / → | Seek backward / forward by the configured interval (default 5s) |
| Shift + ← / → | Seek by 1 second |
| Ctrl + ← / → | Seek by 0.25 seconds |
| J / L | Seek 10 seconds backward / forward |
| ↑ / ↓ | Volume up / down (default step 5%) |
| Mouse scroll wheel (over video) | Volume up / down |
| M | Mute / unmute |
| [ / ] | Decrease / increase playback speed |
| \ | Reset playback speed to 1.0x |
| G / H | Subtitle 100 ms earlier / later |
| Shift + G / H | Subtitle 500 ms earlier / later |
| F or Enter | Toggle fullscreen (covers the entire screen, including the taskbar) |
| Esc | Exit fullscreen |
| Double-click video | Play / Pause |
| T | Toggle Always on Top |
| Ctrl + O | Open media file |
| Right-click | Open the context menu (playback, tracks, subtitles, window, file actions) |

All shortcuts keep working after clicking the video, seek bar, volume slider or any other control -
none of the control-bar elements take keyboard focus, so the window itself always handles playback
keys.

## Settings

Persisted as JSON at `%LOCALAPPDATA%\PetPlayer\settings.json` (seek interval, volume, volume step,
playback speed, always-on-top, window position/size, subtitle preferences, autoplay, auto-load
matching subtitle, controls hide delay, transcript panel visibility, resume positions). Logs go to
`%LOCALAPPDATA%\PetPlayer\logs\`. A corrupted settings file is never fatal - Pet Player falls back
to defaults and keeps a `.corrupt` backup alongside it for diagnostics.

Most settings apply immediately or the next time you open a video. **Subtitle appearance**
settings (font size, vertical position, text opacity, outline) are the one exception - LibVLC only
reads these once, when its video engine starts up, so changes there take effect the next time you
**start Pet Player**, not just the next video you open.

## Known v1 scope notes

A few things called out as optional or "don't over-engineer" in the spec were kept intentionally
simple:

- **Automatic transcription**: `ITranscriptionService` defines the boundary (`video path → timestamped
  transcript`) but has no implementation in v1. Wiring in Whisper / whisper.cpp / faster-whisper
  later doesn't require touching the player, subtitle service, or any view model.
- **Subtitle appearance** (font size, position, opacity, outline) is applied via LibVLC's freetype
  text-renderer options at startup rather than a custom rendering engine, per the spec's explicit
  guidance not to build one. These are core LibVLC engine options with no live-update API, so
  changes in Settings take effect the next time Pet Player is started, not the next video opened.
- **Resume playback** stores a simple `path → last position` map capped at 50 entries; it's off by
  default and toggled in Settings.
- **Playlist** is out of scope for v1, as directed; dropping multiple files loads the first one.

## Verification performed

- `dotnet restore`, `dotnet build -c Release`, and `dotnet publish -r win-x64 --self-contained true`
  all complete with zero errors/warnings, and the published output was inspected to confirm
  `libvlc\win-x64\libvlc.dll`, `libvlccore.dll` and `plugins\` are present alongside `PetPlayer.exe`.
- The generated icon was embedded and extracted back from the compiled `PetPlayer.exe` to confirm
  it renders correctly.
- The full acceptance-test list in the original spec (playing each container format, Explorer
  "Open with", Bengali/space/parenthesis filenames, rapid seeking, etc.) requires a real Windows
  desktop with sample media files and Explorer integration to click through, which this environment
  doesn't have. The code was written and reviewed against every item on that list (clamped seeking,
  malformed-subtitle tolerance, missing-file handling, disposal on reload/exit, single-instance
  hand-off, no-admin registry writes, etc.), but treat the first run on a real target machine as the
  actual acceptance pass, not a formality.
