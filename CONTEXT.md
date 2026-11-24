# CryoUtils Context

## Product

CryoUtils is a standalone Windows desktop utility suite by `ZeroNuxi | ZerroDevs`. It keeps the CryoTray visual language and the same C# / WPF / .NET family conventions, but runs as a standard user utility instead of a hardware control app.

Repository identity: `ZerroDevs/CryoUtils`. Version source: `VERSION` (currently `0.0.100`). License: Apache-2.0.

## Runtime and platform

- Target: `net8.0-windows10.0.19041.0`
- UI: WPF, `UseWPF`, `UseWindowsForms` for tray icon and native color/folder dialogs.
- Platform: x64, Windows 10 2004 or newer for `Windows.Media.Ocr` and Media Foundation MP4.
- Manifest: `asInvoker`, `uiAccess=false`, `PerMonitorV2, PerMonitor`.
- No admin, no driver, no service, no network dependency except the optional GitHub update check.
- Single instance enforced by `Mutex` in `App`.

## Packaging and release

- `Installer/CryoUtils.iss` is an Inno Setup 6.3+ per-user installer. It installs under `%LOCALAPPDATA%\Programs\CryoUtils`, uses `PrivilegesRequired=lowest`, stops a running `CryoUtils.exe` before install/uninstall, and supports silent setup (`/S`) for the updater.
- `build-installer.ps1` reads `VERSION`, publishes the self-contained win-x64 app, compiles the Inno setup, and creates `release\CryoUtils-<version>-win-x64.zip`; both outputs receive `.sha256` files.
- `.github/workflows/release.yml` runs on `VERSION` changes to `main` or manually. It validates the version, builds, compiles the installer, optionally signs with `WINDOWS_SIGNING_PFX_BASE64` and `WINDOWS_SIGNING_PFX_PASSWORD`, and publishes `CryoUtils-Setup-<version>.exe`, the portable zip, and their checksums.
- The baseline is `0.0.100`; increment the patch to `0.0.101`, `0.0.102`, etc. Tags use `v<version>`.
- `CryoUtils.csproj` copies `VERSION`, `LICENSE`, and `NOTICE` to the output and publish folders.

## Architecture

### Composition root

`App.xaml.cs` owns:
- settings load
- localization and theme initialization
- tray icon lifetime
- global hotkey registration and message handling
- capture, recorder, color picker, clipboard history, pinner commands
- update checks and exit cleanup

`App.xaml` merges:
- `Resources/Controls.xaml` (styles and control templates)
- `Resources/Strings.en.xaml` and `Resources/Strings.ar.xaml`
- theme dictionaries (`Themes/Dark.xaml`, `Themes/Light.xaml`)
- `Views/CryoIcons.cs` geometry resources

### Settings

`Services/SettingsService.cs` serializes `Models/AppSettings.cs` to:
`%APPDATA%\CryoUtils\settings.json`

Writes are atomic: temporary file then `File.Move(..., overwrite: true)`. `Save()` raises `SettingsChanged`; `SaveWithoutNotification()` updates the file without re-applying global settings. Export, import and reset use the same model.

Startup defaults: `StartWithWindows=false`, `LaunchInBackground=true`, `RunInTray=true`. `--background` always starts tray-only. `RunInTray=true` turns the window close button into hide; `false` closes the application.

### Theme engine

`Services/ThemeService.cs` holds token dictionaries for Dark, Light and System. It resolves the effective mode from the Windows app theme and rebuilds dynamic resources. All UI uses `DynamicResource` for colors so a live theme switch updates every open window.

Core tokens:

| Token | Dark | Light |
|---|---|---|
| Accent | `#2FD3BD` | `#0E7C6C` |
| Accent strong | `#0F7A6C` | `#0B6455` |
| Window | `#141414` | `#F5F3EF` |
| Surface | `#1E1E1E` | `#FFFFFF` |
| Surface raised | `#242424` | `#F7F4EF` |
| Border | `#333333` | `#DDD7CD` |
| Text primary | `#FFFFFF` | `#1C1A17` |
| Text secondary | `#B8B8B8` | `#5C564D` |

Rules: flat surfaces, 1 px crisp borders, no gradients, no pink or purple, teal as the only signature accent. Radius is small and consistent (`6` to `10`), not pill-shaped. Typography uses Segoe UI Variable / Segoe UI with Cascadia Mono for technical values. Arabic uses the same sizes with `FlowDirection=RightToLeft`; technical tokens (`PNG`, `GIF`, `FPS`, `HEX`, `RGB`, `HSL`, paths, shortcuts) stay LTR.

### Localization

`Services/LocalizationService.cs` loads `Strings.en.xaml` or `Strings.ar.xaml` into the application resources and raises `LanguageChanged`. `ApplyWindow` sets `FlowDirection` on windows. Keys must exist in both dictionaries with identical placeholders. Current parity: 298 keys each.

### Global hotkeys

`Services/GlobalHotkeyService.cs` registers `WM_HOTKEY` against the dashboard handle. Ids:
- capture
- color picker
- window pinner
- plain paste
- clipboard history

`Register` unregisters the previous combination for an id first. `App.ApplyAllSettings` unregisters disabled modules and registers enabled modules. `UnregisterAll` runs on exit.

Defaults:
- Capture: `PrintScreen`, fallback suggestion `Ctrl+Shift+S`
- Color picker: `Ctrl+Shift+C`
- Pinner: `Ctrl+Win+T`
- Plain paste: `Ctrl+Shift+V`
- Clipboard history: `Ctrl+Shift+H`
- Clipboard history module is disabled by default.

### Capture pipeline

1. `CaptureController.StartAsync` freezes the virtual desktop bitmap.
2. `ScreenCaptureService.GetMonitors` enumerates monitors and their DPI scale.
3. One `CaptureOverlayWindow` is created per monitor, positioned in physical pixels with `SetWindowPos` and per-monitor DPI awareness.
4. `CaptureSurface` owns the shared physical-pixel selection rectangle and annotation list.
5. The export path crops the original bitmap, then applies vector annotations and irreversible pixelation in physical coordinates.
6. The overlay closes and returns to the window that was active before capture.

### Recording pipeline

`Views/RecordingBarWindow` drives `Services/RecorderService`.
- Region capture runs on a background STA thread.
- Frames are stored as PNG byte arrays in memory.
- A memory guard stops growth near 380 MB.
- GIF: custom `Services/GifEncoder.cs` GIF89a writer with global palette and per-frame local tables where needed.
- MP4: `Services/Mp4Writer.cs` uses Media Foundation `SinkWriter` with H.264 output. The factory/attribute/sample interfaces are flattened to the native vtable.
- The bar supports timer, FPS, GIF/MP4, pause, stop and save, stop and copy, cancel.
- `Views/RecordingFrameWindow` draws a 2 px non-activating border outside the recorded region.

### Annotation model

`Models/Annotation.cs` stores:
- kind (`Pen`, `Line`, `Arrow`, `Rectangle`, `Highlighter`, `Text`, `StepNumber`, `Blur`)
- color, thickness, opacity
- points, rect, text, font size, RTL flag, step number
- cached pixel preview for blur

`Services/AnnotationRenderer.cs` draws all vectors into a `DrawingVisual` and applies `Blur` through `BitmapEffect` pixelation. Blur is destructive in the exported bitmap, never a reversible overlay on the saved file.

### Other services

- `ScreenCaptureService`: monitor enumeration, GDI capture, window capture, crop, clamp, timestamp names, save dialogs, quick save.
- `ImageIO`: encode PNG/JPEG/WebP, decode bytes, format bytes, resize.
- `CompressionService`: raster and GIF compression, target presets, preview, keep-original fallback.
- `OcrService`: `Windows.Media.Ocr` with installed language packs, including Arabic when available.
- `TextAnalysisService`: code-shape detection by extension/content and color-code discovery for hex, RGB/RGBA and HSL/HSLA.
- `ClipboardService`: hidden `HwndSource` listener, in-memory history, sensitive heuristic, auto-clear timer, plain paste via `SendInput`.
- `TopmostService`: `WS_EX_TOPMOST` toggle, title tracking, optional click-through border and sound.
- `UpdaterService`: GitHub releases API, `.exe`/`.msi` setup asset selection, download, rollback backup, and delayed restart. A setup asset is run with `/S`; a non-setup executable is copied after exit.
- `StartupService`: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, no admin.
- `RecentCaptureService`: in-session thumbnails and paths.
- `TrayIconManager`: tightly cropped packaged CryoUtils tray icon sized from `SystemInformation.SmallIconSize` (16/20/24/32), with a generated fallback; dark menu renderer, left-click action, right-click menu.

## Domain rules

- The reference folder `..\Comile` is read only. Never write, delete or rename there.
- No UAC prompt at launch. Do not switch to `requireAdministrator`.
- Module off means hotkey unregistered and listeners disposed; no polling thread left behind.
- All capture math is in physical pixels. Do not mix WPF device-independent units with the virtual bitmap without scaling.
- Export must be generated from the original frozen frame plus vectors, never from the dimmed overlay.
- Arabic UI must mirror layout but keep technical units and shortcuts LTR. The OCR result keeps code and color tokens LTR even when prose is RTL.
- OCR result text is editable and auto-sized; do not return to a fixed-height text box. `CryoTextBox` defaults its content host to centered, and the OCR window opts into top alignment per control.
- Settings JSON is the only persistent app state. No cloud sync, no telemetry.
- The updater must never replace the running executable directly; it copies a backup and schedules a delayed script after exit.

## Structural decisions

- The dashboard is a single window with six radio-driven panels instead of separate pages. This keeps navigation instant and avoids duplicate state.
- Capture overlays are separate windows per monitor so mixed DPI is handled by the compositor rather than by manual scaling.
- GIF is implemented in-project to avoid a native dependency; WebP uses SkiaSharp.
- MP4 uses Windows Media Foundation instead of bundling FFmpeg.
- Clipboard history is opt-in and memory-only; there is no disk cache.
- Tray menu is native `ContextMenuStrip` with a custom renderer to match the dark surfaces and teal highlight.
