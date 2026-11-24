# CryoUtils Function Registry

Reuse these before creating a duplicate. All services are singletons unless marked static.

## App and lifecycle

| Symbol | Location | Purpose |
|---|---|---|
| `App.CurrentApp` | `App.xaml.cs` | Static access to the running application. |
| `App.OnStartup` | `App.xaml.cs` | Loads settings, applies theme and language, creates tray, registers hotkeys, starts listeners. `--background` always stays in the tray; otherwise `LaunchInBackground` decides whether the dashboard opens. |
| `App.OnExit` | `App.xaml.cs` | Saves window position, unregisters hotkeys, disposes tray and services. |
| `App.ApplyAllSettings` | `App.xaml.cs` | Re-applies theme, language, hotkeys, pin border settings, clipboard listener and module registration. |
| `App.StartCapture` | `App.xaml.cs` | Opens capture overlays and hides the dashboard. |
| `App.StartRecording` | `App.xaml.cs` | Opens capture overlays in auto-record mode. |
| `App.OpenColorPicker` | `App.xaml.cs` | Hides dashboard, snapshots the desktop, opens `ColorPickerWindow`. |
| `App.OpenClipboardHistory` | `App.xaml.cs` | Opens the clipboard history window when the module is on. |
| `App.TogglePinnerFromDashboard` | `App.xaml.cs` | Hides dashboard, waits for focus transfer, toggles topmost on the foreground window. |
| `App.ShowDashboard` | `App.xaml.cs` | Shows, restores and activates the dashboard. |
| `App.ShowToast` | `App.xaml.cs` | Shows a localized transient toast unless notifications are off. |
| `App.ExitApplication` | `App.xaml.cs` | Restores pinned windows, disposes services and shuts down. |

## Settings and startup

| Symbol | Location | Purpose |
|---|---|---|
| `SettingsService.Instance` | `Services/SettingsService.cs` | Shared settings singleton. |
| `SettingsService.Load` | same | Reads JSON or returns defaults. |
| `SettingsService.Save` | same | Atomic write then raises `SettingsChanged`. |
| `SettingsService.SaveWithoutNotification` | same | Atomic write without re-applying settings. |
| `SettingsService.Export` | same | Writes the current model to a chosen JSON path. |
| `SettingsService.Import` | same | Replaces the model from JSON and saves. |
| `SettingsService.Reset` | same | Restores `new AppSettings()` and saves. |
| `SettingsService.RegisterRecentColor` | same | Deduplicates, prepends and caps recent colors at 10. |
| `SettingsService.WriteAtomicJson` | same | Shared atomic JSON writer. |
| `StartupService.IsEnabled` | `Services/StartupService.cs` | Reads the HKCU Run value. |
| `StartupService.SetEnabled` | same | Writes or deletes the HKCU Run value. |

## Theme and localization

| Symbol | Location | Purpose |
|---|---|---|
| `ThemeService.Initialize` | `Services/ThemeService.cs` | Loads the configured theme and builds dictionaries. |
| `ThemeService.Apply` | same | Applies Dark, Light or System live. |
| `ThemeService.Normalize` | same | Maps a stored mode to a valid mode. |
| `ThemeService.GetColor` | same | Returns a token color. |
| `ThemeService.GetBrush` | same | Returns a token brush for code-created UI. |
| `ThemeService.IsLight`, `IsHighContrast` | same | Current effective state. |
| `ThemeService.Shutdown` | same | Clears theme resources. |
| `LocalizationService.Apply` | `Services/LocalizationService.cs` | Loads EN or AR dictionary into application resources. |
| `LocalizationService.ApplyWindow` | same | Sets flow direction and re-applies window resources. |
| `LocalizationService.Get` | same | Looks up a key, falls back to the key text. |
| `LocalizationService.Format` | same | Looks up and formats with culture-safe arguments. |

## Capture and imaging

| Symbol | Location | Purpose |
|---|---|---|
| `CaptureController.Instance` | `Views/CaptureController.cs` | Capture session singleton. |
| `CaptureController.StartAsync` | same | Freezes the virtual desktop and opens overlays. |
| `CaptureController.StartRecordingMode` | same | Capture with auto-record after selection. |
| `CaptureController.CloseAll` | same | Closes every overlay and restores focus. |
| `CaptureController.ExportSelection` | same | Crops, renders annotations, copies or saves. |
| `CaptureController.OpenOcrForSelection` | same | Runs OCR on the current selection and opens the result window. |
| `CaptureController.PinSelection` | same | Creates a pinned image window. |
| `ScreenCaptureService.GetMonitors` | `Services/ScreenCaptureService.cs` | Returns monitor bounds, work areas and DPI scale. |
| `ScreenCaptureService.CaptureRegion` | same | GDI virtual-screen or monitor region capture. |
| `ScreenCaptureService.Crop` | same | Crops a bitmap by physical rect. |
| `ScreenCaptureService.ClampRect` | same | Keeps a rect inside image bounds. |
| `ScreenCaptureService.CaptureWindow` | same | `PrintWindow` capture of a window handle. |
| `ScreenCaptureService.GetWindowTitle` | same | Window title helper. |
| `ScreenCaptureService.CreateTimestampedFileName` | same | `CryoUtils_yyyy-MM-dd_HH-mm-ss.ext`. |
| `ScreenCaptureService.SaveWithDialog` | same | Save dialog plus format-specific encoding. |
| `ScreenCaptureService.QuickSave` | same | Writes to the effective save folder. |
| `ImageIO.SaveBitmap` | `Services/ImageIO.cs` | Dispatch by extension: PNG, JPEG, WebP. |
| `ImageIO.ExtensionForFormat` | same | Normalizes `PNG`, `JPG`, `WEBP`. |
| `ImageIO.SaveWebP`, `EncodeWebP` | same | SkiaSharp WebP encode. |
| `ImageIO.EncodePng` | same | WPF PNG encode. |
| `ImageIO.DecodeBytes` | same | Decode with `OnLoad` and freeze. |
| `ImageIO.FormatBytes` | same | Human size string. |
| `ImageIO.Resize` | same | Scale a bitmap for compression previews. |
| `AnnotationRenderer.Draw` | `Services/AnnotationRenderer.cs` | Draws pen, line, arrow, rectangle, highlighter, text, step badge and cached blur. |
| `AnnotationRenderer.BuildPixelation` | same | Builds irreversible pixelated bitmap for a rect. |
| `AnnotationRenderer.ParseColor` | same | Hex to `Color`, safe fallback. |
| `GifWriter` | `Services/GifEncoder.cs` | Streaming GIF89a writer. |
| `GifWriter.AddFrame` | same | Adds a PNG-framed bitmap. |
| `GifWriter.Finish` | same | Writes trailer and palette blocks. |
| `GifWriter.EncodePngFramesToGif` | same | One-call encode from PNG byte frames. |
| `Mp4Writer.Write` | `Services/Mp4Writer.cs` | Media Foundation H.264 encode from `RecordingResult`. |

## Recording

| Symbol | Location | Purpose |
|---|---|---|
| `RecorderService.Start` | `Services/RecorderService.cs` | Starts region capture, hides the cursor, allocates frame list. |
| `RecorderService.Pause`, `Resume` | same | Pause and resume capture. |
| `RecorderService.SetFps` | same | Applies a new FPS to the live session. |
| `RecorderService.Cancel` | same | Stops and drops frames. |
| `RecorderService.PngFrames` | same | Access to captured frame bytes. |
| `RecorderService.IsRecording`, `IsPaused`, `IsEncoding` | same | Live state. |
| `RecorderService.Dispose` | same | Stops timers and releases the capture thread. |
| `RecordingBarWindow` | `Views/RecordingBarWindow.xaml.cs` | Timer, FPS, format, pause, stop/save, stop/copy, cancel. |
| `RecordingFrameWindow` | `Views/RecordingFrameWindow.cs` | Click-through red border around the recording region. |

## Tools

| Symbol | Location | Purpose |
|---|---|---|
| `ColorPickerWindow` | `Views/ColorPickerWindow.xaml.cs` | Magnifier, HEX/RGB/HSL, copy format, close. |
| `TopmostService.ToggleFocusedWindow` | `Services/TopmostService.cs` | Toggles `WS_EX_TOPMOST` on the foreground window. |
| `TopmostService.Unpin` | same | Removes topmost and hides the border. |
| `TopmostService.ApplySettings` | same | Reconciles border and sound preferences. |
| `TopmostService.RestoreForExit` | same | Restores topmost state on app exit. |
| `PinnedBorderWindow` | `Views/PinnedBorderWindow.cs` | Click-through 2 px topmost tracking border. |
| `CompressionService.CompressAsync` | `Services/CompressionService.cs` | Compress raster or GIF with target preset. |
| `CompressionResult` | same | Data, extension, dimensions, preview, original size and saved percent. |
| `ClipboardService.SetEnabled` | `Services/ClipboardService.cs` | Creates or destroys the hidden clipboard listener. |
| `ClipboardService.CopyItem` | same | Restores text or image to the clipboard. |
| `ClipboardService.PlainPaste` | same | Puts plain text and sends Ctrl+V. |
| `ClipboardService.Remove`, `Clear` | same | Remove one item or all history. |
| `ClipboardService.Add` (internal) | same | Adds an item, detects sensitive text, starts auto-clear timer, trims to limit. |
| `ClipItem` | same | Text/image item with display, metadata and sensitivity. |
| `OcrService.RecognizeAsync` | `Services/OcrService.cs` | Windows OCR on a bitmap; null or localized failure on unsupported language. |
| `TextAnalysisService.LooksLikeCode` | `Services/TextAnalysisService.cs` | Detects code by file extension or code-shaped lines; Markdown paths stay prose. |
| `TextAnalysisService.FindColorCodes` | same | Finds distinct hex, RGB/RGBA and HSL/HSLA tokens and converts them to `Color` values. |
| `TextColorCode` | same | Token text plus parsed color for the OCR swatches. |
| `UpdaterService.CurrentVersion` | `Services/UpdaterService.cs` | Reads the stamped informational version, falling back to `0.0.100`. |
| `UpdaterService.CheckAsync` | same | GitHub release check and `.exe`/`.msi` asset selection. |
| `UpdaterService.DownloadAsync` | same | Download asset to temp with progress. |
| `UpdaterService.CreateBackup` | same | Copy the current executable and version into the rollback folder. |
| `UpdaterService.ApplyUpdate` | same | Backup, run a setup with `/S` or copy an executable after exit, then restart. |
| `UpdaterService.RestoreBackup` | same | Restore the previous executable. |
| `UpdaterService.DeleteBackup` | same | Remove rollback backup. |
| `UpdaterService.OpenReleasePage` | same | Open the release page or the releases index. |

## Hotkeys and native interop

| Symbol | Location | Purpose |
|---|---|---|
| `GlobalHotkeyService.Register` | `Services/GlobalHotkeyService.cs` | Registers one id after unregistering its old combination. |
| `GlobalHotkeyService.Unregister`, `UnregisterAll` | same | Removes one or all hotkeys. |
| `GlobalHotkeyService.IsRegistered` | same | Used by the PrintScreen banner. |
| `GlobalHotkeyService.TryCaptureShortcut` | same | Maps WPF key and modifiers to tokens; standalone keys are F1 to F12, PrintScreen and Home. |
| `GlobalHotkeyService.FormatShortcutDisplay` | same | Human-readable shortcut, LTR. |
| `NativeMethods.GetWindowLongPtr`, `SetWindowLongPtr` | `Services/NativeMethods.cs` | Extended window style access. |
| `NativeMethods.SetWindowPos` | same | Topmost and physical placement. |
| `NativeMethods.SendPlainPasteShortcut` | same | `SendInput` Ctrl+V. |
| `NativeMethods.PrintWindow` | same | Window capture. |
| `NativeMethods.RegisterHotKey`, `UnregisterHotKey` | same | Hotkey registration. |
| `NativeMethods.GetDpiForMonitor` | same | Per-monitor scale. |

## UI components and handlers

| Component | Key behavior |
|---|---|
| `MainWindow.xaml` | Six tab radio buttons, title bar, module cards, capture settings, tools, hotkeys, general, about. |
| `MainWindow.xaml.cs` | `ReloadUi`, `ShowTab`, `ModuleToggle_Changed`, `Setting_Changed`, `CmbTheme_SelectionChanged`, `CmbLanguage_SelectionChanged`, `RefreshHotkeyRows`, `AddHotkeyRow`, `RefreshRecentStrip`, `LoadCompressionSource`, `BtnCompress_Click`, `RefreshPinnedState`, `RefreshClipboardState`, `BtnExportSettings_Click`, `BtnImportSettings_Click`, `BtnResetSettings_Click`, update handlers, `TitleBar_MouseLeftButtonDown`, `BtnClose_Click`, `Window_Closing` (hide to tray unless shutdown is in progress). |
| `CaptureOverlayWindow.xaml` | Selection canvas, top-left size badge, magnifier, vertical tool rail, horizontal action rail, tooltips. |
| `CaptureOverlayWindow.xaml.cs` | Selection drag/move/resize, handles, double-click window, Ctrl+A, annotation tool selection, color/width, undo/redo, text editor, blur commit, OCR/pin/print/save/copy shortcuts, Esc. |
| `CaptureSurface.cs` | Shared physical selection, annotation list, hit testing, cursor state, redraw. |
| `HotkeyCaptureWindow.xaml` | Modal key capture with modifier display and validation. |
| `OcrResultWindow.xaml` | Auto-height, top-aligned editable OCR text; code/wrap toggles, line/character metadata, color swatches, Open/Save as, drag/drop for Markdown and all text types. |
| `ClipboardHistoryWindow.xaml` | Searchable list, copy, remove, clear, sensitive note. |
| `PinWindow.xaml` | Frameless always-on-top image, drag, wheel zoom, Ctrl+wheel opacity, right-click menu. |
| `ToastWindow.xaml` | Non-intrusive toast, bottom-right, click-through. |
| `TrayIconManager.cs` | Packaged CryoUtils icon (with generated fallback), dark menu, capture, record, color picker, dashboard, settings, exit, left-click action; `BuildMenu` and `RebuildMenu` keep labels and RTL in sync with language changes. |
| `CryoIcon` / `IconData` | Vector geometry resources used by XAML. |

## Reuse rules

- Use `ImageIO` for every encode, decode, resize and byte-size format.
- Use `ScreenCaptureService` for every physical-pixel capture, crop and clamp.
- Use `AnnotationRenderer` for every annotation export.
- Use `ThemeService.GetBrush` and DynamicResource tokens only. Do not hardcode colors.
- Use `LocalizationService.Get` and `Format` for every visible string.
- Use `TextAnalysisService` for OCR code detection and color-code parsing instead of duplicating regexes in a window.
- Use `SettingsService` for persistence; do not write a second JSON store.
- Use `GlobalHotkeyService` for every global shortcut; do not create a second hotkey window.
- Use `RecordingBarWindow` and `RecorderService` for every recording path.
- Use `ClipboardService` for history, plain paste and clip restoration.
