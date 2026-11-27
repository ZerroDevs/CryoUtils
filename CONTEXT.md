# CryoUtils Context

## Product

CryoUtils is a standalone Windows desktop utility suite by `ZeroNuxi | ZerroDevs`. It keeps the CryoTray visual language and the same C# / WPF / .NET family conventions, but runs as a standard user utility instead of a hardware control app.

Repository identity: `ZerroDevs/CryoUtils`. Version source: `VERSION` (currently `0.0.102`). License: Apache-2.0.

## Runtime and platform

- Target: `net10.0-windows` (`UseWPF=true`, `UseWindowsForms=false`, zero `SkiaSharp` or `Microsoft.Windows.SDK.NET`).
- UI & Shell: Pure WPF paired with Win32 `Shell_NotifyIconW` dark owner-drawn tray menu (`Tray/TrayIconManager.cs`), Win32 `ChooseColorW` (`Services/ColorDialogHelper.cs`), and `HWND_MESSAGE` hotkeys (`Services/GlobalHotkeyService.cs`).
- Imaging & Media: Windows WIC (`ImageIO.cs`, `ImageProcessingService.cs`, `CompressionService.cs`), managed RIFF/VP8L encoder (`Services/SimpleWebpEncoder.cs`), compressed zlib PDF builder (`Services/SimplePdfWriter.cs`), direct WinRT COM ABI interop (`Services/WinRtBridge.cs`), and native Media Foundation H.264/AAC encoding (`Services/Recording/`, `Services/MediaConversionService.cs`).
- Platform: x64, Windows 10 2004 or newer for `Windows.Media.Ocr`, `Windows.Data.Pdf`, and `Windows.Graphics.Capture`.
- Manifest: `asInvoker`, `uiAccess=false`, `PerMonitorV2, PerMonitor`.
- No admin, no driver, no service. Network access is limited to optional GitHub update checks/downloads and explicit, consented uploads to catbox.moe.
- Single-instance tray guard (`Local\CryoUtils.SingleInstance.v1` mutex + `Local\CryoUtils.Activate.v1` `EventWaitHandle` in `App.xaml.cs`) focuses the running instance instead of spawning duplicate tray icons, while short-lived `--action`, `--capture`, `--uninstall-cleanup`, and `--unregister-context-menu` helpers stay exempt.

## Packaging and release

- `Installer/CryoUtils.iss` is an Inno Setup 6.3+ per-user installer supporting `Full` (self-contained offline) and `Lite` (`CryoUtils-Setup-Lite-<version>.exe`, framework-dependent with automatic `.NET 10 Desktop Runtime` download if missing and leftover runtime DLL cleanup). It installs under `%LOCALAPPDATA%\Programs\CryoUtils`, uses `PrivilegesRequired=lowest`, compresses `{app}` with silent `compact.exe /c /s:"{app}" /i /q`, stops a running `CryoUtils.exe` before install/uninstall, and supports `/VERYSILENT` for the variant-aware updater.
- `build-installer.ps1 -Variant All|Full|Lite` reads `VERSION`, publishes win-x64 Full and Lite builds, compiles `CryoUtils-Setup-<version>.exe` and `CryoUtils-Setup-Lite-<version>.exe`, and creates `release\CryoUtils-<version>-win-x64.zip` + `release\CryoUtils-Lite-<version>-win-x64.zip` with `.sha256` files.
- `.github/workflows/release.yml` runs on `VERSION` changes to `main` or manually. It validates the version, builds, compiles Full and Lite installers, optionally signs executables and installers with `WINDOWS_SIGNING_PFX_BASE64` and `WINDOWS_SIGNING_PFX_PASSWORD`, uses `ReleaseNotes/v<version>.md` when present, and publishes installers, portable zips, and checksums.
- Baseline is `0.0.102`; increment the patch to `0.0.103`, `0.0.104`, etc. Tags use `v<version>`.
- `CryoUtils.csproj` copies `VERSION`, `LICENSE`, and `NOTICE` to the output and publish folders and embeds a compact `256x256` `CryoUtils-Icon.png`.

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

Companion local stores: `capture-library.json` (library metadata), `workflows.json` (recipes), `upload-history.json` (links and local expiry), `clipboard.bin` (optional DPAPI text persistence), and `logs/cryoutils.log` (bounded diagnostics). Capture files themselves stay in the configured save folder.

Startup defaults: `StartWithWindows=false`, `LaunchInBackground=true`. `--background` always starts tray-only. The title-bar X and Alt+F4 always hide the dashboard; quitting is only available from the tray's Exit action. The capture pixel magnifier is opt-in with `ShowCaptureLoupe=false`; enable it on the Capture tab.

### Theme engine

`Services/ThemeService.cs` holds token dictionaries for Dark, Light and System plus 6 `AccentPresets` (`CryoBlue` default, `Cyan`, `Emerald`, `Violet`, `Amber`, `Rose`). It resolves the effective mode from the Windows app theme and rebuilds dynamic resources (including frozen `HeroBackgroundGradientBrush`). All UI uses `DynamicResource` for colors so a live theme or accent switch updates every open window.

Core tokens:

| Token                             | Dark                                          | Light                                         |
| --------------------------------- | --------------------------------------------- | --------------------------------------------- |
| Accent                            | `#2B88D8`                                     | `#1B6CB3`                                     |
| Accent strong / button            | `#1E6FAE`                                     | `#1B6CB3`                                     |
| Accent text                       | `#6FB8F2`                                     | `#1B6CB3`                                     |
| Capture / Media / Color / Utility | `#4FC3F7` / `#7CB342` / `#F3C765` / `#62B0E8` | `#0B6E99` / `#4C7A22` / `#8A6500` / `#1D6FA5` |
| Window / Header                   | `#141414` / `#1B1B1B`                         | `#F5F3EF` / `#EDEAE4`                         |
| Surface                           | `#1E1E1E`                                     | `#FFFFFF`                                     |
| Surface raised                    | `#242424`                                     | `#F7F4EF`                                     |
| Border                            | `#333333`                                     | `#DDD7CD`                                     |
| Text primary                      | `#FFFFFF`                                     | `#1C1A17`                                     |
| Text secondary                    | `#B8B8B8`                                     | `#5C564D`                                     |

Rules: CryoTray-aligned `141414` / `1E1E1E` / `242424` neutral surfaces, 1 px `CornerRadius="0"` Cryo Blue window border (`#2B88D8` / `#1B6CB3`), purposeful hero gradient (`#1E4F7A -> #18649C`), and semantic 4-accent domain coding matching CryoTray (`Capture`, `Media`, `Color`, `Utility`).Typography uses Segoe UI Variable / Segoe UI with Cascadia Mono for technical values. Arabic uses the same sizes with `FlowDirection=RightToLeft`; filenames, sizes, color codes, PNG/GIF/FPS, paths, and shortcuts stay LTR.

### Localization

`Services/LocalizationService.cs` loads `Strings.en.xaml` or `Strings.ar.xaml` into the application resources and raises `LanguageChanged`. `ApplyWindow` sets `FlowDirection` on windows. Keys must exist in both dictionaries with identical placeholders. Current parity: 1014 keys each.

### QR Code & Utilities Suite

`Services/QrCodeService.cs` provides managed QR encoding and decoding via `ZXing.Net` 0.16.11 without external cloud or native dependencies. Scanned QR selections persist to `RecentCaptureService` and disk automatically; generated text QR results are shown and can be saved explicitly. `Views/QrResultWindow.xaml` displays the decoded payload, format, and the crisp QR code image thumbnail with quick image saving. `Services/ColorContrastService.cs` calculates WCAG 2.1 relative luminance, contrast ratio, and AA/AAA conformance evaluations. `Views/CountdownWindow.xaml` provides non-activating floating countdown pills for delayed capture.

### Window Pinner

`Services/TopmostService.cs` manages multiple pinned windows with per-window opacity (`SetLayeredWindowAttributes`), click-through (`WS_EX_TRANSPARENT`), accent borders (`Views/PinnedBorderWindow.cs`), an exclusion list, and best-effort title/process restore. `--safe-mode` releases the saved topmost session before normal startup.
Validation (`IsValidTargetWindow`) requires a valid non-empty window title and prevents pinning Desktop (`Progman`, `WorkerW`), Taskbars (`Shell_TrayWnd`), Shell windows, or CryoUtils itself.
`ForceClearAll()` provides emergency recovery by unpinning, closing borders, and invoking `RedrawWindow` across the entire screen.
Exiting the app automatically invokes `Unpin(silent: true)`, clearing `HWND_NOTOPMOST`, restoring window styles, and closing border windows.

### Global hotkeys

`Services/GlobalHotkeyService.cs` registers `WM_HOTKEY` against an `HWND_MESSAGE` sink. Ids:

- capture (`CaptureHotkeyId`)
- repeat last region (`RepeatLastRegionHotkeyId`)
- active-window snap (`ActiveWindowSnapHotkeyId`)
- color picker (`ColorPickerHotkeyId`)
- window pinner (`WindowPinnerHotkeyId`)
- plain paste (`PlainPasteHotkeyId`)
- clipboard history (`ClipboardHistoryHotkeyId`)
- Cryo Workbench (`WorkbenchHotkeyId`)
- instant replay (`ReplayHotkeyId`)
- live draw (`LiveDrawHotkeyId`)

`Register` unregisters the previous combination for an id first. `App.ApplyAllSettings` unregisters disabled modules and registers enabled modules. `UnregisterAll` runs on exit.

Defaults:

- Capture: `PrintScreen`, fallback suggestion `Ctrl+Shift+S`
- Snap active window (`0 ms`, no overlay): `Ctrl+Alt+S`
- Color picker: `Ctrl+Shift+C`
- Pinner: `Ctrl+Win+T`
- Plain paste: `Ctrl+Shift+V`
- Clipboard history: `Ctrl+Shift+H`
- Cryo Workbench: `Ctrl+Shift+W`
- Instant Replay: `Ctrl+Shift+R`
- Live Draw: `Ctrl+Shift+D`
- Clipboard history module is disabled by default.

### Capture pipeline

1. `CaptureController.StartAsync` freezes the virtual desktop bitmap.
2. `ScreenCaptureService.GetMonitors` enumerates monitors and their DPI scale.
3. One `CaptureOverlayWindow` is created per monitor, positioned in physical pixels with `SetWindowPos` and per-monitor DPI awareness.
4. `CaptureSurface` owns the shared physical-pixel selection rectangle and annotation list.
5. The export path crops the original bitmap, then applies vector annotations and irreversible pixelation in physical coordinates.
6. The overlay closes and returns to the window that was active before capture.

### Recording pipeline

`Views/RecordingBarWindow` drives two paths. GIF still uses `Services/RecorderService` and the in-project `Services/GifEncoder.cs` writer; MP4 uses the live recorder in `Services/Recording/`.

- `WgcCaptureEngine` captures the monitor containing the selection with `Windows.Graphics.Capture` and a Direct3D 11 frame pool, then crops to the selected physical region. `D3D11Readback` copies the GPU surface into a CPU-readable staging texture; `SoftwareBitmap` is the compatibility fallback. `GdiCaptureEngine` is the 30 FPS fallback, including cross-monitor selections.
- `FrameComposer` scales/crops into the output size and draws the optional cursor highlight, click ripples, and webcam bubble.
- `MfEncoder` writes H.264/AAC directly to a temporary MP4 through Media Foundation `SinkWriter`. It prefers hardware transforms, retries in software, and sets input and output media types separately. Audio is optional; if the AAC encoder or device is unavailable the file is video-only and the bar reports it.
- `WasapiCaptureService` captures default-device system loopback and microphone; `AudioMixer` normalizes to 48 kHz stereo PCM.
- `RecordingSession` owns capture, timestamps, pause/resume, audio, cursor effects, and webcam composition. Pause removes paused time from encoded timestamps. Stop finalizes the temp MP4 before the trim/save step.
- `ReplaySegmentRing` writes complete, keyframe-aligned MP4 segments under `%LOCALAPPDATA%\CryoUtils\ReplayBuffer`. `ReplayService` keeps the configured 15 seconds–10 minutes, seals the partial segment when the user saves, and pins snapshot files while `ReplayRemuxer` copies compressed samples into one MP4. A `MediaComposition` re-encode is the fallback.
- The bar supports timer, FPS, GIF/MP4, pause, stop and save, stop and copy, cancel. `Views/RecordingTrimWindow` previews and trims an MP4 before saving. `Views/RecorderSettingsWindow` edits format, resolution, FPS, quality/bitrate, audio, effects, webcam, trim, and replay settings.
- `Views/RecordingFrameWindow` draws a 2 px non-activating border outside the recorded region.

### Workspace overlays and tools

`App` owns the modeless workspace windows: `LiveDrawWindow` (transparent virtual-screen ink layer using `LiveInkSurface`), `ScreenRulerWindow` (transparent measurement overlay using `RulerSurface`), `FileShelfWindow`, `ImageEditorWindow`, and `DeveloperToolboxWindow`. Live Draw and Screen Ruler close before a normal capture so their chrome cannot leak into the frozen desktop. Live Draw captures by hiding the overlay, taking a GDI desktop capture, and compositing the ink items into a new PNG; the ordinary annotation overlay still starts from a frozen frame. File Shelf queues compress/convert work through `FileActionQueueService` and PDF work through `PdfToolsService`. The Overview Activity Center reads `CaptureLibraryService`, `UploadHistoryService`, `WorkflowService.Runs`, `FileActionQueueService.Jobs`, `TopmostService.PinnedWindows`, and storage totals; it is read-only.

### Annotation model

`Models/Annotation.cs` stores:

- kind (`Pen`, `Line`, `Arrow`, `Rectangle`, `Ellipse`, `Spotlight`, `Redaction`, `Highlighter`, `Text`, `Blur`, `Zoom`, `Step`)
- color, thickness, opacity
- points, rect, text, font size, RTL flag, step number
- cached pixel preview for blur

`Services/AnnotationRenderer.cs` draws all vectors into a `DrawingVisual`, renders `Zoom` as a 2.0x cropped circular lens from `sourceBitmap`, and applies `Blur` through irreversible pixelation in the exported bitmap.

### Other services

- `ScreenCaptureService`: monitor enumeration, GDI capture, window capture, crop, clamp, timestamp names, save dialogs, quick save.
- `ImageIO`: encode PNG/JPEG/WebP, decode bytes, format bytes, resize.
- `CompressionService`: raster and GIF compression, target presets, preview, keep-original fallback.
- `OcrService`: `Windows.Media.Ocr` with installed language packs, including Arabic when available; joins `OcrResult.Lines` to preserve line breaks.
- `TextAnalysisService`: code-shape detection, syntax tokenization and color-code discovery for hex, RGB/RGBA and HSL/HSLA.
- `ClipboardService`: hidden `HwndSource` listener, in-memory history by default, optional DPAPI text persistence, named snippets, transforms, image OCR, sensitive heuristic, auto-clear timer, plain paste via `SendInput`.
- `CaptureLibraryService`: capture source metadata, tags, favorites, and Recycle Bin deletion; no duplicate capture store.
- `WorkflowService`: post-capture recipes matched by source application and run after Copy/Save.
- `UploadProviderService` / `UploadHistoryService`: Catbox or a local folder, consent state, history, expiry, and deletion limits.
- `FileActionQueueService`: convert/compress/recycle jobs with dry run, overwrite policy, retry, and cancellation.
- `ColorStudioService`: palette extraction, contrast pairs, token exports, and color-vision simulation.
- `DiagnosticsService`: bounded logs, exception logging, safe mode, and the Workbench log viewer.
- `ScrollingCaptureService`: frame collection, row-overlap detection, and bounded vertical stitching.
- `PdfToolsService` / `MediaConversionService`: page ranges, merge/split/reorder/rotate/OCR, video trim, and GIF frame ranges.
- `TopmostService`: `WS_EX_TOPMOST` toggle, title tracking, optional click-through border and sound.
- `UpdaterService`: GitHub releases API over HTTPS, edition-matched `CryoUtils-Setup-*` vs `CryoUtils-Setup-Lite-*` `.exe`/`.msi` asset selection, mandatory `.sha256` verification before launch (deleting on missing/malformed/mismatched hash), rollback backup, and delayed `/VERYSILENT` setup script.
- `StartupService`: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, no admin.
- `RecentCaptureService`: in-session thumbnails and paths.
- `TrayIconManager`: tightly cropped packaged CryoUtils tray icon sized from `SystemInformation.SmallIconSize` (16/20/24/32), with a generated fallback; dark menu renderer, left-click action, right-click menu.

## Domain rules

- The reference folder `..\Comile` is read only. Never write, delete or rename there.
- No UAC prompt at launch. Do not switch to `requireAdministrator`.
- Module off means hotkey unregistered and listeners disposed; no polling thread left behind.
- All capture math is in physical pixels. Do not mix WPF device-independent units with the virtual bitmap without scaling.
- Export must be generated from the original frozen frame plus vectors, never from the dimmed overlay.
- The saved-capture limit manages only `CryoUtils_*` files, moves items to the Recycle Bin, and asks before a manual limit reduction removes anything. Never delete unrelated files in the chosen save folder.
- Arabic UI must mirror layout but keep technical units and shortcuts LTR. The OCR result keeps code and color tokens LTR even when prose is RTL.
- OCR result text is editable and auto-sized in `CryoRichTextBox`; code mode applies syntax colors while preserving the caret and selection. Do not return to a fixed-height or plain-text result box.
- Capture chrome must not overlap: choose a free action-rail slot around the tool rail, and hide the optional loupe when no free slot exists.
- Settings and the explicit local stores above are the only persistent app state. No cloud sync and no telemetry; uploads create external Catbox links only when the user explicitly requests them, and the local provider never uses the network.
- The updater must never replace the running executable directly; it copies a backup and schedules a delayed script after exit.

## Structural decisions

- The dashboard is a single resizable `480x565` (`480x450` in Screenshot-Only Lite mode, `MinWidth=430`, `MinHeight=360`, persisted once resized via `WindowSizeCustomized`/`WindowWidth`/`WindowHeight`) CryoTray-style borderless window with a 38 px minimalist icon title bar (`Ctrl+K` `TxtQuickSearch` + `SearchPopup`, `BtnQuit`, `BtnClose`), a flush underline tab strip (`GroupName="MainNav"`, `TabButtonStyle`) across **Overview**, **Capture**, **Tools**, **Workspace**, **Hotkeys**, and **Settings** (with integrated About & Updates), and a bottom corner resize grip.
- The Overview tab features a full-bleed CryoTray PCB Hero Banner (`#255D8E -> #1E9FD0`, procedural PCB traces + ExtraLight hotkey trigger), glowing `42x42` Aperture / Full-Suite strip with `48x24` `ON`/`OFF` pill toggle, clean `1 px` hairline utility rows (auto-collapsed in Screenshot-Only Lite mode), and a compact Recent Captures strip (`Session` / `All time`) with drag-out.
- Capture overlays are separate windows per monitor so mixed DPI is handled by the compositor rather than by manual scaling.
- The larger Cryo Workbench tools live inside `MainWindow`'s consolidated **Workspace** tab (`Views/Tabs/WorkbenchTabView.xaml(.cs)`) with flush sub-tabs for Library, Workflows, Privacy, File Actions, Color, Media, Pinner, and Diagnostics (replacing the separate `PowerToolsWindow`).
- Library metadata references files in the configured save folder rather than copying them into a hidden store. Deletion uses the Recycle Bin.
- Scrolling capture stitches in memory with a 16,000 px cap; the control bar is positioned outside the selection when the work area allows it.
- GIF is implemented in-project and lossless WebP uses `SimpleWebpEncoder` (zero `SkiaSharp`).
- MP4 uses direct Windows Media Foundation interop (`MfInterop` / `WinRtBridge`) instead of bundling FFmpeg or `Microsoft.Windows.SDK.NET`.
- Clipboard history is opt-in and memory-only by default. Encrypted text persistence is a separate explicit setting and can be disabled.
- Tray menu is a Win32 owner-drawn dark/RTL popup (`Tray/TrayIconManager.cs`, zero WinForms) with cached theme colors and `HandlePrimaryClick` capture/dashboard routing.
