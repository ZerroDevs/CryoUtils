<p align="center">
  <img src="Images/CryoUtils-Icon.png" alt="CryoUtils" width="72" height="72">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue" alt="Windows 10 / 11">
  <img src="https://img.shields.io/badge/.NET-8.0%20WPF%20%2B%20WinForms-purple" alt=".NET 8.0 WPF + WinForms">
  <img src="https://img.shields.io/badge/License-Apache--2.0-blue" alt="Apache-2.0">
</p>

<p align="center">
  <strong>A native Windows capture, OCR, and desktop utility suite by ZeroNuxi under ZerroDevs.</strong>
</p>

CryoUtils keeps screenshot capture, annotation, OCR, color picking, image compression, clipboard history, and window pinning in one small tray application. Capture a region or a window, mark it up, copy or save it, pull text out of it locally, or record it as a GIF or MP4. It runs as a standard user, keeps its data on the machine, and has no account, telemetry, or background service.

The current baseline is `0.0.100`. The application runs locally from the tray and does not require administrator elevation.

## Contents

- [At a glance](#at-a-glance)
- [Feature tour](#feature-tour)
- [Capture and annotations](#capture-and-annotations)
- [OCR](#ocr)
- [Color picker](#color-picker)
- [Image compressor](#image-compressor)
- [Clipboard history](#clipboard-history)
- [Window pinner](#window-pinner)
- [Tray, startup, and appearance](#tray-startup-and-appearance)
- [Updates](#updates)
- [Install and run](#install-and-run)
- [Build from source](#build-from-source)
- [Versioning and releases](#versioning-and-releases)
- [Testing and diagnostics](#testing-and-diagnostics)
- [Data, privacy, and local files](#data-privacy-and-local-files)
- [Architecture](#architecture)
- [Project layout](#project-layout)
- [Limitations](#limitations)
- [Troubleshooting](#troubleshooting)
- [Security policy](#security-policy)
- [Contributing](#contributing)
- [License and attribution](#license-and-attribution)

## At a glance

| Item | Details |
| --- | --- |
| Platform | Windows 10 version 2004 (build 19041) or Windows 11, x64 |
| Runtime | .NET 8 with WPF and Windows Forms |
| Privileges | Standard user (`asInvoker`); no UAC and no service |
| Capture | Multi-monitor freeze frame, region or window selection, annotations |
| Output | PNG, JPG, WebP, GIF, and MP4 |
| OCR | Local Windows OCR with editable results, code mode, color swatches, and text-file open/save |
| Tools | Color picker, compressor, clipboard history, window pinner |
| Appearance | Dark (default), Light, or System; English and Arabic with RTL |
| Storage | `%APPDATA%\CryoUtils\settings.json` plus user-chosen output files |
| Network use | GitHub Releases update checks and downloads only |
| Installer | Per-user Inno Setup installer; no elevation |
| License | Apache License 2.0 |

## Feature tour

### Dashboard

The dashboard is a compact WPF window with six tabs: **Overview**, **Capture**, **Tools**, **Hotkeys**, **General**, and **About**. It shows module cards, current versions, recent captures, quick actions, and the state of the tray modules.

The dashboard uses the shared CryoUtils surface language: flat cards, one-pixel borders, a teal accent, and small radii. It follows the selected theme and language at runtime.

### Capture and annotations

The capture hotkey is configurable and defaults to `PrintScreen`. If Windows already owns that key, the dashboard suggests `Ctrl+Shift+S`. A standalone `HOME` key is also accepted by the hotkey picker.

Capture opens one overlay per monitor over a frozen desktop snapshot. The current monitor's overlay supports:

- Drag selection with a live size badge, moving, eight resize handles, and double-click window selection.
- `Ctrl+A` for the full monitor, `Esc` to close, and the existing capture shortcuts.
- Pen, line, arrow, rectangle, highlighter, text, step-number, and irreversible blur/pixelate annotations.
- Color swatches, a custom color picker, and thickness control.
- Undo and redo.
- Copy, save, print, pin to screen, and OCR actions.
- Recording a selected region as GIF or MP4, with FPS and duration controls.

Saving uses a save dialog or the configured quick-save folder. The default output folder is `Pictures\CryoUtils`.

### OCR

OCR uses the Windows OCR engine and the language packs already installed in Windows. The selected region is read locally; no image or text is uploaded.

The **Text from selection** window is more than a read-only preview:

- Short results use an auto-sized, top-aligned text box instead of a large empty area.
- The text is editable and can be copied, opened from a text/code/Markdown file, saved as a text file, or loaded by drag/drop.
- **Code mode** switches to a monospace font and horizontal scrolling; **Wrap lines** controls wrapping for prose.
- Line and character counts are shown in the header.
- Hex, RGB/RGBA, and HSL/HSLA color codes are detected and listed as clickable swatches. Clicking a swatch copies that code.
- Markdown files open as editable source text; CryoUtils does not render formatted Markdown.

OCR availability depends on the Windows language packs. English and Arabic are detected when the corresponding OCR language is installed.

### Color picker

The color picker freezes the desktop, shows a magnifier grid, and reports the selected color as HEX, RGB, and HSL. Clicking copies the selected format; the choice is saved. Recent colors appear in the dashboard Tools tab.

The default hotkey is `Ctrl+Shift+C`. The module can be disabled from the dashboard, which unregisters its hotkey.

### Image compressor

The compressor accepts PNG, JPG/JPEG, WebP, and GIF files by drag/drop or file browser. It provides:

- Discord, WhatsApp, and custom presets.
- Quality and scale controls for custom compression.
- Before/after sizes, saved percentage, and a preview.
- Save as, copy file, and a keep-original fallback when re-encoding would be larger.

### Clipboard history

Clipboard history is opt-in and memory-only. When enabled, it records text and image clips, supports search, restores an item to the clipboard, and can remove one item or clear the list. A heuristic marks password, token, API key, and similar text as sensitive and can schedule automatic removal.

The module is disabled by default. Disabling it stops the clipboard listener and clears the in-memory list. A separate plain-text paste hotkey strips formatting and sends `Ctrl+V` to the focused application.

### Window pinner

The window pinner toggles `WS_EX_TOPMOST` on the focused window through the default `Ctrl+Win+T` hotkey or the dashboard Tools card. An optional click-through border tracks the pinned window, with an optional confirmation sound.

### Tray, startup, and appearance

The tray icon is the normal entry point:

- Left-click opens the dashboard by default; it can also be configured to start capture.
- The tray menu provides capture, recording, color picker, dashboard, and exit actions.
- **Run in background** is on by default, so a normal launch starts in the tray.
- The title-bar **X** always hides the dashboard and keeps the tray process alive. Quit only from the tray's **Exit** action.
- **Run on startup** writes a per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry and passes `--background`. It is off by default.

Dark is the default appearance. Light and System modes use the same resource tokens and apply live. Arabic switches layouts to RTL while technical values and shortcuts stay LTR.

## Updates

The About tab checks the latest GitHub release for `ZerroDevs/CryoUtils`. When a newer release is available, CryoUtils downloads the release's setup executable or MSI, saves a rollback copy of the current executable when that option is enabled, and starts the per-user installer after the application exits.

The release assets include SHA-256 checksum files. Verify a downloaded installer or portable archive manually if you want a stronger integrity check before running it:

```powershell
Get-FileHash .\CryoUtils-Setup-0.0.100.exe -Algorithm SHA256
```

The in-app updater relies on HTTPS GitHub delivery; it does not currently verify the checksum or Authenticode signature itself. Signing is applied to release assets when the optional signing secrets are configured in GitHub Actions.

## Install and run

### Installer

Download `CryoUtils-Setup-<version>.exe` from [GitHub Releases](https://github.com/ZerroDevs/CryoUtils/releases), optionally verify its `.sha256` file, and run it.

The installer is per-user and installs to:

```text
%LOCALAPPDATA%\Programs\CryoUtils
```

It creates Start Menu and optional desktop shortcuts and does not request administrator elevation. It closes a running CryoUtils process before replacing files. Uninstalling removes the application and shortcuts; `%APPDATA%\CryoUtils\settings.json` is left in place.

### Portable zip

Download `CryoUtils-<version>-win-x64.zip`, verify its `.sha256` file, and extract it. The release is self-contained for win-x64, so a separate .NET runtime is not required. Run `CryoUtils.exe` from the extracted folder.

The portable archive includes `VERSION`, `LICENSE`, and `NOTICE`. The in-app update path downloads the per-user installer rather than replacing a portable folder in place.

### Requirements

- Windows 10 version 2004 (build 19041) or newer, or Windows 11, x64.
- No administrator privileges.
- Optional: additional Windows OCR language packs for OCR in those languages.
- Internet access only for update checks and downloads.

### First run

The first launch starts in the tray when **Run in background** is enabled (the default). Open the dashboard from the tray icon, or disable **Run in background** in General to open it on launch.

## Build from source

### Prerequisites

- Windows 10/11 x64.
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Optional, for installers: [Inno Setup 6.3 or newer](https://jrsoftware.org/isdl.php). `build-installer.ps1` can install it through winget if it is missing.

### Build

```powershell
dotnet restore CryoUtils.csproj
dotnet build CryoUtils.csproj -c Release
```

The debug executable is written to:

```text
bin\Debug\net8.0-windows10.0.19041.0\CryoUtils.exe
```

Run a local debug build directly, or start it in the tray:

```powershell
.\bin\Debug\net8.0-windows10.0.19041.0\CryoUtils.exe
.\bin\Debug\net8.0-windows10.0.19041.0\CryoUtils.exe --background
```

### Build the installer and portable zip

With Inno Setup available:

```powershell
.\build-installer.ps1
```

The script publishes a self-contained win-x64 build and creates:

```text
Installer\Output\CryoUtils-Setup-<version>.exe
release\CryoUtils-<version>-win-x64.zip
```

Both files receive a matching `.sha256` checksum. The Inno script is `Installer\CryoUtils.iss`; it packages the publish output plus `LICENSE`, `NOTICE`, and `VERSION`.

## Versioning and releases

The root `VERSION` file contains exactly three numeric parts. `CryoUtils.csproj` reads it into the assembly, file, and informational versions.

- Current baseline: `0.0.100`.
- Next releases: `0.0.101`, `0.0.102`, and so on.
- Tags: `v0.0.100`, `v0.0.101`, and so on.

A push to `main` that changes `VERSION`, or a manual **Release** workflow run, validates the version, builds the Release application, publishes a self-contained win-x64 payload, compiles the Inno installer with Inno Setup, creates checksummed setup and portable assets, and publishes a GitHub release for the matching tag.

Signing is optional. When `WINDOWS_SIGNING_PFX_BASE64` and `WINDOWS_SIGNING_PFX_PASSWORD` repository secrets are configured, the app and installer are signed and verified; otherwise the workflow publishes unsigned assets and emits a warning.

## Testing and diagnostics

CryoUtils does not currently include a built-in automated test harness or a `/test` command. Before a pull request or release, build the Release configuration and exercise the affected path manually:

```powershell
dotnet build CryoUtils.csproj -c Release
```

Useful checks include startup and tray lifetime, capture selection and copy, annotation export, OCR on an installed language pack, GIF/MP4 output, compressor formats, clipboard capture, pinned-window borders, English/Arabic switching, and theme switching.

Crash paths preserve the session and show a localized error toast; settings writes are atomic and failures do not stop the running session. There is no diagnostic dump command in this repository.

## Data, privacy, and local files

CryoUtils does not include telemetry, analytics, advertising, accounts, or a remote service.

Local files:

- `%APPDATA%\CryoUtils\settings.json`: settings, window position, recent colors, and update preferences.
- `%LOCALAPPDATA%\CryoUtils\Rollback\`: optional rollback executable and version file created before an update.
- `%TEMP%\CryoUtils\update\`: downloaded updater files.
- The configured save folder, defaulting to `Pictures\CryoUtils`: images and recordings you choose to save.

Recent capture thumbnails and clipboard history are held in memory only and are cleared when the process exits or the corresponding module is disabled.

Update checks contact:

```text
https://api.github.com/repos/ZerroDevs/CryoUtils/releases/latest
```

The request is a standard GitHub API call and does not include screenshots, OCR text, settings, or usage data.

## Architecture

CryoUtils is one WPF process with a tray lifetime and short-lived capture windows. The application root is `App.xaml.cs`; `SettingsService`, `ThemeService`, `LocalizationService`, and `GlobalHotkeyService` are the shared application services.

```text
CryoUtils.exe
├── App.xaml.cs                     startup, tray lifecycle, capture, settings, updates
├── Views/
│   ├── MainWindow                  dashboard and six panels
│   ├── CaptureOverlayWindow        per-monitor capture surface
│   ├── OcrResultWindow             editable OCR results and text tools
│   ├── ColorPickerWindow           magnifier and color formats
│   ├── ClipboardHistoryWindow      searchable clipboard list
│   ├── RecordingBarWindow          GIF/MP4 recording controls
│   └── PinWindow / PinnedBorder    pinned images and topmost window border
├── Services/
│   ├── ScreenCaptureService        monitor enumeration and physical-pixel capture
│   ├── AnnotationRenderer          vector annotations and destructive blur
│   ├── GifEncoder / Mp4Writer      GIF89a and Media Foundation output
│   ├── OcrService                  Windows.Media.Ocr
│   ├── TextAnalysisService         code detection and color-code parsing
│   ├── CompressionService          image compression and preview
│   ├── ClipboardService            opt-in in-memory clipboard history
│   ├── TopmostService              window pinning
│   ├── UpdaterService              GitHub release checks and restart flow
│   └── Settings / Theme / Startup  local preferences and presentation
└── Tray/TrayIconManager.cs         tray icon menu and entry points
```

Key choices:

- Capture coordinates are physical pixels; WPF device-independent units are not mixed into capture math.
- Annotations are rendered from the original frozen frame for export, not from the dimmed overlay.
- The last 10 recent captures are held in memory for the dashboard strip.
- The compressor keeps the source when an encode would be larger.
- The shared theme uses dynamic resources, so an open window updates during a live theme switch.
- Arabic mirrors layout while technical tokens remain LTR.
- Settings are written atomically through a temporary file and a move.

## Project layout

```text
CryoUtils.csproj                  .NET 8 WPF/WinForms project and metadata
VERSION                           release version consumed by MSBuild and the installer
LICENSE                           Apache License 2.0
NOTICE                            attribution and redistribution notice
app.manifest                      asInvoker and per-monitor DPI settings
build-installer.ps1               self-contained publish, Inno compile, portable zip
Installer/CryoUtils.iss           per-user Inno Setup definition
.github/workflows/release.yml     Windows release packaging and GitHub release publication
Models/                           settings, capture tools, annotations
Services/                         capture, OCR, image, clipboard, updates, settings
Tray/                             system tray icon and menu
Views/                            dashboard, overlays, result and tool windows
Resources/                       XAML controls and English/Arabic dictionaries
Images/                           application icon
```

## Limitations

- **Windows only.** The application depends on WPF, Windows Forms, Windows OCR, Media Foundation, and Win32 APIs.
- **OCR language packs vary.** Windows OCR can only recognize languages installed for OCR; a missing pack causes a localized OCR failure.
- **PrintScreen is often reserved.** Use the suggested `Ctrl+Shift+S` fallback or another combination when registration fails.
- **Mixed-DPI capture has not been fully exercised.** The test environment uses two 1920 x 1080 monitors at 100% scaling.
- **Recorded GIF/MP4 files have not been visually played back** in this environment; the encoders were verified by output.
- **No built-in automated test suite.** Verification is currently build plus manual exercise.
- **Update integrity is manual.** The in-app updater downloads the release setup over HTTPS but does not enforce the published checksum or a signature. Verify downloads manually when needed.
- **Portable updates install per-user.** A portable copy does not update its own folder in place; the update button starts the per-user setup.
- **Markdown is source text.** The OCR result window opens and edits Markdown but does not render it.
- **No fan, GPU, power, or driver control.** CryoUtils is a desktop utility, not a hardware-control tool.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| Capture hotkey does nothing | If `PrintScreen` is owned by Windows, use the dashboard fallback `Ctrl+Shift+S` or choose another key in Hotkeys. |
| Only one monitor appears | Confirm Windows is extending the desktop and both monitors are active; overlays are created per active monitor. |
| OCR reports no text | Check that the selected region contains readable text and the required Windows OCR language pack is installed. |
| OCR window looks like an old fixed-height dialog | Rebuild/run the current `0.0.100` build; the current window auto-sizes to its content. |
| GIF or MP4 is empty | Keep the recording above one frame and confirm the selected region is non-zero. |
| Clipboard history is empty | Enable the clipboard module; history is memory-only and clears when the module is disabled. |
| Tray icon is hidden | Open the Windows notification-area overflow and enable the CryoUtils icon. |
| Dashboard does not open on launch | **Run in background** is on by default; open it from the tray or turn that setting off. |
| Update check fails | Confirm access to GitHub Releases and try the manual Release page. |
| Installer asks for elevation | Use the current per-user installer; older or unrelated packages may require elevation. |
| Arabic layout is clipped | Switch language again to reapply the resource dictionary and report the window name. |

## Security policy

Please report security problems privately through GitHub's private vulnerability reporting or by email. See [SECURITY.md](SECURITY.md) for supported versions, response expectations, and the local threat model.

## Contributing

Bug reports, localization fixes, and focused pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) for build commands, architectural rules, localization/theme conventions, versioning, and the PR checklist.

## License and attribution

CryoUtils is licensed under the [Apache License, Version 2.0](LICENSE). See [NOTICE](NOTICE) for attribution and redistribution requirements.

Copyright 2026 ZeroNuxi - ZerroDevs.

The application was created by ZeroNuxi under the ZerroDevs organization. Redistributions and derivative works must retain the attribution notice in [NOTICE](NOTICE).
