# CryoUtils Function Registry

Reuse these before creating a duplicate. All services are singletons unless marked static.

## App and lifecycle

| Symbol                          | Location      | Purpose                                                                                                                                                                                                     |
| ------------------------------- | ------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `App.CurrentApp`                | `App.xaml.cs` | Static access to the running application.                                                                                                                                                                   |
| `App.OnStartup`                 | `App.xaml.cs` | Loads settings, applies theme and language, creates tray, registers hotkeys, starts listeners. `--background` always stays in the tray; `--safe-mode` disables pinner restore and releases saved topmost windows; otherwise `LaunchInBackground` decides whether the dashboard opens. |
| `App.OnExit`                    | `App.xaml.cs` | Saves window position, unregisters hotkeys, disposes tray and services.                                                                                                                                     |
| `App.ApplyAllSettings`          | `App.xaml.cs` | Re-applies theme, language, hotkeys, pin border settings, clipboard listener and module registration.                                                                                                       |
| `App.StartCapture`              | `App.xaml.cs` | Opens capture overlays and hides the dashboard.                                                                                                                                                             |
| `App.StartRecording`            | `App.xaml.cs` | Opens capture overlays in auto-record mode.                                                                                                                                                                 |
| `App.OpenColorPicker`           | `App.xaml.cs` | Hides dashboard, snapshots the desktop, opens `ColorPickerWindow`.                                                                                                                                          |
| `App.OpenClipboardHistory`      | `App.xaml.cs` | Opens the clipboard history window when the module is on.                                                                                                                                                   |
| `App.TogglePinnerFromDashboard` | `App.xaml.cs` | Hides dashboard, waits for focus transfer, toggles topmost on the foreground window.                                                                                                                        |
| `App.ShowDashboard`             | `App.xaml.cs` | Shows, restores and activates the dashboard.                                                                                                                                                                |
| `App.ShowToast`                 | `App.xaml.cs` | Shows a localized transient toast unless notifications are off.                                                                                                                                             |
| `App.ExitApplication`           | `App.xaml.cs` | Restores pinned windows, disposes services and shuts down.                                                                                                                                                  |

## Settings and startup

| Symbol                                                                                   | Location                             | Purpose                                                                                                                                                               |
| ---------------------------------------------------------------------------------------- | ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SettingsService.Instance`                                                               | `Services/SettingsService.cs`        | Shared settings singleton.                                                                                                                                            |
| `SettingsService.Load`                                                                   | same                                 | Reads JSON or returns defaults.                                                                                                                                       |
| `SettingsService.Save`                                                                   | same                                 | Atomic write then raises `SettingsChanged`.                                                                                                                           |
| `SettingsService.SaveWithoutNotification`                                                | same                                 | Atomic write without re-applying settings.                                                                                                                            |
| `SettingsService.Export`                                                                 | same                                 | Writes the current model to a chosen JSON path.                                                                                                                       |
| `SettingsService.Import`                                                                 | same                                 | Replaces the model from JSON and saves.                                                                                                                               |
| `SettingsService.Reset`                                                                  | same                                 | Restores `new AppSettings()` and saves.                                                                                                                               |
| `SettingsService.RegisterRecentColor`                                                    | same                                 | Deduplicates, prepends and caps recent colors at 10.                                                                                                                  |
| `SettingsService.WriteAtomicJson`                                                        | same                                 | Shared atomic JSON writer.                                                                                                                                            |
| `StartupService.IsEnabled`                                                               | `Services/StartupService.cs`         | Reads the HKCU Run value.                                                                                                                                             |
| `StartupService.SetEnabled`                                                              | same                                 | Writes or deletes the HKCU Run value.                                                                                                                                 |
| `ScreenCaptureService` (`QuickSave`, `GetSavedCaptureFiles`, `GetManagedCaptureFiles`, `EnforceSavedCaptureLimit`) | `Services/ScreenCaptureService.cs` | Saves unique timestamped captures, enumerates saved captures newest-first, and prunes only `CryoUtils_*` files beyond `AppSettings.SavedCapturesLimit` into the Recycle Bin. |
| `CryoContextMenuService.IsEnabled`                                                       | `Services/CryoContextMenuService.cs` | Checks whether Windows Explorer cascading context menu integration is enabled in HKCU.                                                                                |
| `CryoContextMenuService.SetEnabled`                                                      | same                                 | Toggles full registration / unregistration across Images, GIF, PDF, Video, Audio, Data/Text, All Files (`*`), and Folders (`Directory`).                              |
| `CryoContextMenuService.RegisterAll`                                                     | same                                 | Registers direct cascading submenus with `SubCommands=""`, `CommandFlags=0x20` dividers, nested `Resize >` group, and `CryoUtilsConvert` sibling for >16 image items. |
| `CryoContextMenuService.UnregisterAll`                                                   | same                                 | Cleans up all `SystemFileAssociations`, `*`, and `Directory` context menu keys from HKCU and notifies Explorer via `SHChangeNotify`.                                  |
| `CloudUploadService.UploadImageAsync`                                                    | `Services/CloudUploadService.cs`     | Encodes screenshot bitmap to PNG and streams to Catbox with `IProgress<UploadProgressInfo>` byte reporting and cancellation.                                          |
| `CloudUploadService.UploadFileAsync`                                                     | same                                 | Streams any disk file (`FileStream`) up to Catbox's 200 MB limit with real-time byte progress and cancellation.                                                       |
| `PdfToolsService.PdfToImagesAsync`                                                       | `Services/PdfToolsService.cs`        | Uses `Windows.Data.Pdf.PdfDocument` to render PDF pages to high-res PNG/JPG without external DLLs.                                                                    |
| `PdfToolsService.ImagesToPdf`                                                            | same                                 | Uses `SkiaSharp.SKDocument.CreatePdf` to bundle multiple images into a single PDF.                                                                                    |
| `PdfToolsService.CompressPdfAsync`                                                       | same                                 | Compresses PDF pages and embedded raster streams.                                                                                                                     |
| `PdfToolsService.ConvertPdfToGrayscaleAsync`                                             | same                                 | Renders pages with Rec.709 luminance grayscale matrix for print-ready smaller PDFs.                                                                                   |
| `PdfToolsService.RotatePdfAsync`                                                         | same                                 | Rotates all PDF pages (`90` or `180` degrees) cleanly via SkiaSharp canvas transform.                                                                                 |
| `ImageProcessingService.ConvertFormat`                                                   | `Services/ImageProcessingService.cs` | Converts between PNG, JPG, WebP, ICO, BMP, and JFIF with optimal compression presets.                                                                                 |
| `ImageProcessingService.RemoveBackground`                                                | same                                 | Smart corner-sampled Euclidean color distance background eraser generating transparent PNG.                                                                           |
| `ImageProcessingService.StripExif`                                                       | same                                 | Re-encodes image to scrub EXIF, camera, GPS, and creation metadata.                                                                                                   |
| `ImageProcessingService.ResizePreset`                                                    | same                                 | Resizes images to `50pct`, `w1920`, or `s512` using high-quality SkiaSharp resampling.                                                                                |
| `ImageProcessingService.TrimTransparentMargins`                                          | same                                 | Crops bounding non-transparent pixel margins cleanly into PNG.                                                                                                        |
| `ImageProcessingService.FlattenWhite`                                                    | same                                 | Composites transparent pixels onto solid white background and exports JPG.                                                                                            |
| `ImageProcessingService.ExtractGifFrames`                                                | same                                 | Decodes animated GIF frames respecting `SKCodecFrameInfo` disposal into sequential PNGs.                                                                              |
| `ImageProcessingService.ToBase64DataUri`                                                 | same                                 | Encodes image file as `data:image/<mime>;base64,...` URI string.                                                                                                      |
| `ImageProcessingService.LoadWpfBitmap`                                                   | same                                 | Loads frozen WPF `BitmapSource` without locking source file on disk (SkiaSharp fallback for WebP).                                                                    |
| `MediaConversionService.VideoToAudioAsync`                                               | `Services/MediaConversionService.cs` | Extracts MP3 audio using Windows native `MediaTranscoder` without FFmpeg.                                                                                             |
| `MediaConversionService.ConvertAudioAsync`                                               | same                                 | Converts audio between MP3, WAV, and M4A (`MediaTranscoder`).                                                                                                         |
| `MediaConversionService.CompressVideoForDiscordAsync`                                    | same                                 | Computes target bitrate from duration to target <10 MB MP4 via `MediaTranscoder`.                                                                                     |
| `MediaConversionService.MuteVideoAsync`                                                  | same                                 | Silences video via `MediaClip.Volume = 0` + `MediaComposition.CreateDefaultEncodingProfile()`.                                                                        |
| `MediaConversionService.SaveThumbnailAsync`                                              | same                                 | Captures thumbnail JPG frame from video via `MediaComposition.GetThumbnailAsync`.                                                                                     |
| `MediaConversionService.VideoToGifAsync`                                                 | same                                 | Decodes video frames via `MediaComposition` and renders animated GIF via `GifWriter`.                                                                                 |
| `FileToolsService.FormatJson` / `FormatXml`                                              | `Services/FileToolsService.cs`       | Prettifies or minifies JSON (`System.Text.Json`) and prettifies XML (`XDocument`).                                                                                    |
| `FileToolsService.ComputeHash`                                                           | same                                 | Streams file hash (`SHA256` / `MD5`) with constant memory overhead.                                                                                                   |
| `FileToolsService.CollectSortedImages`                                                   | same                                 | Collects supported images naturally ordered via `NativeMethods.StrCmpLogicalW`.                                                                                       |
| `FileToolsService.FolderImagesToGif`                                                     | same                                 | Fits mixed folder images into uniform GIF canvas and encodes via `GifWriter`.                                                                                         |
| `FileToolsService.BatchCompressFolderAsync`                                              | same                                 | Compresses all top-level images in folder into `_compressed` subfolder using current preset settings.                                                                 |

## Theme and localization

| Symbol                                   | Location                          | Purpose                                               |
| ---------------------------------------- | --------------------------------- | ----------------------------------------------------- |
| `ThemeService.Initialize`                | `Services/ThemeService.cs`        | Loads the configured theme and builds dictionaries.   |
| `ThemeService.Apply`                     | same                              | Applies Dark, Light or System live.                   |
| `ThemeService.Normalize`                 | same                              | Maps a stored mode to a valid mode.                   |
| `ThemeService.GetColor`                  | same                              | Returns a token color.                                |
| `ThemeService.GetBrush`                  | same                              | Returns a token brush for code-created UI.            |
| `ThemeService.IsLight`, `IsHighContrast` | same                              | Current effective state.                              |
| `ThemeService.Shutdown`                  | same                              | Clears theme resources.                               |
| `LocalizationService.Apply`              | `Services/LocalizationService.cs` | Loads EN or AR dictionary into application resources. |
| `LocalizationService.ApplyWindow`        | same                              | Sets flow direction and re-applies window resources.  |
| `LocalizationService.Get`                | same                              | Looks up a key, falls back to the key text.           |
| `LocalizationService.Format`             | same                              | Looks up and formats with culture-safe arguments.     |

## Capture and imaging

| Symbol                                           | Location                             | Purpose                                                                                    |
| ------------------------------------------------ | ------------------------------------ | ------------------------------------------------------------------------------------------ |
| `CaptureController.Instance`                     | `Views/CaptureController.cs`         | Capture session singleton.                                                                 |
| `CaptureController.UploadSelectionAsync`         | same                                 | Checks upload settings/consent, optionally archives the selection, uploads it to Catbox, and copies the direct link. |
| `UploadConsentService.EnsureUploadAllowed`       | `Services/UploadConsentService.cs`   | Enforces the enable toggle and one-time catbox.moe consent prompt.                         |
| `CloudUploadService.UploadImageAsync`            | `Services/CloudUploadService.cs`     | Anonymous multipart/form-data upload returning direct link.                                |
| `CryoContextMenuService.SetEnabled`              | `Services/CryoContextMenuService.cs` | Registers/unregisters HKCU cascading context menu entries for PDF, image, and video files. |
| `PdfToolsService.PdfToImagesAsync`               | `Services/PdfToolsService.cs`        | Native Windows.Data.Pdf page renderer to high-res PNG/JPG images.                          |
| `PdfToolsService.ImagesToPdf`                    | same                                 | SkiaSharp multi-image PDF generator.                                                       |
| `PdfToolsService.CompressPdfAsync`               | same                                 | Re-encodes PDF pages at optimized DPI and compression.                                     |
| `ImageProcessingService.ConvertFormat`           | `Services/ImageProcessingService.cs` | Formats: PNG, JPG, WebP, ICO, BMP, JFIF.                                                   |
| `ImageProcessingService.RemoveBackground`        | same                                 | Color-distance alpha matting for transparent PNG cutout.                                   |
| `ImageProcessingService.StripExif`               | same                                 | Strips GPS and camera metadata without touching pixels.                                    |
| `MediaConversionService.VideoToAudioAsync`       | `Services/MediaConversionService.cs` | Windows MediaTranscoder MP3 audio track extractor.                                         |
| `MediaConversionService.VideoToGifAsync`         | same                                 | Windows MediaComposition frame extraction to GifWriter.                                    |
| `CaptureController.StartAsync`                   | same                                 | Freezes the virtual desktop and opens overlays.                                            |
| `CaptureController.StartForImage`                | same                                 | Opens an existing capture in screenshot mode with full annotation and action rails.        |
| `CaptureController.StartRecordingMode`           | same                                 | Capture with auto-record after selection.                                                  |
| `CaptureController.QrSelection`                  | same                                 | Scans QR code from selected region and displays result window.                             |
| `CaptureController.CloseAll`                     | same                                 | Closes every overlay and restores focus.                                                   |
| `CaptureController.ExportSelection`              | same                                 | Crops, renders annotations, copies or saves.                                               |
| `CaptureController.OpenOcrForSelection`          | same                                 | Runs OCR on the current selection and opens the result window.                             |
| `CaptureController.PinSelection`                 | same                                 | Creates a pinned image window.                                                             |
| `ScreenCaptureService.GetMonitors`               | `Services/ScreenCaptureService.cs`   | Returns monitor bounds, work areas and DPI scale.                                          |
| `ScreenCaptureService.CaptureRegion`             | same                                 | GDI virtual-screen or monitor region capture.                                              |
| `ScreenCaptureService.Crop`                      | same                                 | Crops a bitmap by physical rect.                                                           |
| `ScreenCaptureService.ClampRect`                 | same                                 | Keeps a rect inside image bounds.                                                          |
| `ScreenCaptureService.CaptureWindow`             | same                                 | `PrintWindow` capture of a window handle.                                                  |
| `ScreenCaptureService.GetWindowTitle`            | same                                 | Window title helper.                                                                       |
| `ScreenCaptureService.CreateTimestampedFileName` | same                                 | `CryoUtils_yyyy-MM-dd_HH-mm-ss.ext`.                                                       |
| `ScreenCaptureService.SaveWithDialog`            | same                                 | Save dialog plus format-specific encoding.                                                 |
| `ScreenCaptureService.QuickSave`                 | same                                 | Writes to the effective save folder.                                                       |
| `ImageIO.SaveBitmap`                             | `Services/ImageIO.cs`                | Dispatch by extension: PNG, JPEG, WebP.                                                    |
| `ImageIO.ExtensionForFormat`                     | same                                 | Normalizes `PNG`, `JPG`, `WEBP`.                                                           |
| `ImageIO.SaveWebP`, `EncodeWebP`                 | same                                 | SkiaSharp WebP encode.                                                                     |
| `ImageIO.EncodePng`                              | same                                 | WPF PNG encode.                                                                            |
| `ImageIO.DecodeBytes`                            | same                                 | Decode with `OnLoad` and freeze.                                                           |
| `ImageIO.FormatBytes`                            | same                                 | Human size string.                                                                         |
| `ImageIO.Resize`                                 | same                                 | Scale a bitmap for compression previews.                                                   |
| `AnnotationRenderer.Draw`                        | `Services/AnnotationRenderer.cs`     | Draws pen, line, arrow, rectangle, highlighter, text, step badge and cached blur.          |
| `AnnotationRenderer.BuildPixelation`             | same                                 | Builds irreversible pixelated bitmap for a rect.                                           |
| `AnnotationRenderer.ParseColor`                  | same                                 | Hex to `Color`, safe fallback.                                                             |
| `GifWriter`                                      | `Services/GifEncoder.cs`             | Streaming GIF89a writer.                                                                   |
| `GifWriter.AddFrame`                             | same                                 | Adds a PNG-framed bitmap.                                                                  |
| `GifWriter.Finish`                               | same                                 | Writes trailer and palette blocks.                                                         |
| `GifWriter.EncodePngFramesToGif`                 | same                                 | One-call encode from PNG byte frames.                                                      |
| `Mp4Writer.Write`                                | `Services/Mp4Writer.cs`              | Media Foundation H.264 encode from `RecordingResult`.                                      |

## Recording

| Symbol                                                  | Location                      | Purpose                                                        |
| ------------------------------------------------------- | ----------------------------- | -------------------------------------------------------------- |
| `RecorderService.Start`                                 | `Services/RecorderService.cs` | Starts region capture, hides the cursor, allocates frame list. |
| `RecorderService.Pause`, `Resume`                       | same                          | Pause and resume capture.                                      |
| `RecorderService.SetFps`                                | same                          | Applies a new FPS to the live session.                         |
| `RecorderService.Cancel`                                | same                          | Stops and drops frames.                                        |
| `RecorderService.PngFrames`                             | same                          | Access to captured frame bytes.                                |
| `RecorderService.IsRecording`, `IsPaused`, `IsEncoding` | same                          | Live state.                                                    |
| `RecorderService.Dispose`                               | same                          | Stops timers and releases the capture thread.                  |
| `RecordingOptions.FromSettings`                         | `Services/Recording/RecordingOptions.cs` | Normalizes FPS, size, quality/bitrate, audio, effects, and replay settings. |
| `ReplayMath`                                            | same                          | Retention count, estimated disk use, 2x free-space preflight, and clip start. |
| `MfEncoder`                                             | `Services/Recording/MfEncoder.cs` | Live H.264/AAC SinkWriter with hardware/software fallback and silent-audio fill. |
| `WgcCaptureEngine` / `GdiCaptureEngine`                 | `Services/Recording/CaptureEngine.cs` | WGC/D3D11 monitor capture cropped to the selection; 30 FPS GDI fallback. |
| `D3D11Readback`                                         | `Services/Recording/D3D11Readback.cs` | GPU-to-staging BGRA readback for WGC frames; SoftwareBitmap fallback. |
| `FrameComposer`                                         | `Services/Recording/FrameComposer.cs` | Scale/crop plus cursor highlight, click pulses, and webcam bubble. |
| `WasapiCaptureService` / `AudioMixer`                   | `Services/Recording/WasapiCaptureService.cs`, `AudioMixer.cs` | Loopback/microphone capture at 48 kHz stereo and clipping-safe mixing. |
| `RecordingSession`                                      | `Services/Recording/RecordingSession.cs` | Normal MP4 capture, pause/resume, finalize, and error state. |
| `ReplaySegmentRing`                                     | `Services/Recording/ReplaySegmentRing.cs` | Keyframe-aligned disk segments, retention, current-segment seal, and pinning. |
| `ReplayRemuxer`                                         | `Services/Recording/ReplayRemuxer.cs` | Compressed sample remux; `MediaComposition` re-encode fallback. |
| `ReplayService`                                         | `Services/Recording/ReplayService.cs` | Always-on buffer lifecycle, disk preflight, hotkey save, and capture-library entry. |
| `RecordingTrimWindow` / `RecorderSettingsWindow`        | `Views/`                       | Trim-before-save and recorder/replay settings UI.              |

## QR Code and Color Contrast

| Symbol                                    | Location                           | Purpose                                                                       |
| ----------------------------------------- | ---------------------------------- | ----------------------------------------------------------------------------- |
| `QrCodeService.Decode`                    | `Services/QrCodeService.cs`        | Decodes QR/barcode from `BitmapSource` using pure managed ZXing.Net.          |
| `QrCodeService.Encode`                    | same                               | Generates QR code `BitmapSource` with configurable ECC level and size.        |
| `ColorContrastService.CalculateLuminance` | `Services/ColorContrastService.cs` | Calculates WCAG 2.1 relative luminance for a color.                           |
| `ColorContrastService.CalculateRatio`     | same                               | Calculates contrast ratio between two colors (1:1 to 21:1).                   |
| `ColorContrastService.Evaluate`           | same                               | Returns `ContrastEvaluation` with AA/AAA pass/fail for normal and large text. |
| `ColorContrastService.FormatRatio`        | same                               | Formats ratio to string (e.g. `4.50:1`).                                      |
| `RecordingBarWindow`                      | `Views/RecordingBarWindow.xaml.cs` | Timer, FPS, format, pause, stop/save, stop/copy, cancel.                      |
| `RecordingFrameWindow`                    | `Views/RecordingFrameWindow.cs`    | Click-through red border around the recording region.                         |

## Tools

| Symbol                               | Location                          | Purpose                                                                                                            |
| ------------------------------------ | --------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `ColorPickerWindow`                  | `Views/ColorPickerWindow.xaml(.cs)` | Stationary draggable inspector with per-monitor frozen desktop eyedropper backdrop (`StartSamplingAsync` / `StopSampling`), 15x15 magnifier, HEX/RGB/HSL row copy, WCAG contrast lock, and re-pick button. |
| `TopmostService.ToggleFocusedWindow` | `Services/TopmostService.cs`      | Toggles `WS_EX_TOPMOST` on the foreground window with title/class validation.                                      |
| `TopmostService.Unpin`               | same                              | Removes topmost, restores styles, hides border, forces screen redraw.                                              |
| `TopmostService.ForceClearAll`       | same                              | Emergency recovery tool: unpins, destroys border, and clears whole-screen artifacts.                               |
| `TopmostService.ApplySettings`       | same                              | Reconciles border and sound preferences.                                                                           |
| `TopmostService.RestoreForExit`      | same                              | Restores topmost state on app exit.                                                                                |
| `PinnedBorderWindow`                 | `Views/PinnedBorderWindow.cs`     | Click-through 2 px topmost tracking border.                                                                        |
| `CompressionService.CompressAsync`   | `Services/CompressionService.cs`  | Compress raster or GIF with target preset.                                                                         |
| `CompressionResult`                  | same                              | Data, extension, dimensions, preview, original size and saved percent.                                             |
| `ClipboardService.SetEnabled`        | `Services/ClipboardService.cs`    | Creates or destroys the hidden clipboard listener.                                                                 |
| `ClipboardService.CopyItem`          | same                              | Restores text or image to the clipboard.                                                                           |
| `ClipboardService.PlainPaste`        | same                              | Puts plain text and sends Ctrl+V.                                                                                  |
| `ClipboardService.Remove`, `Clear`   | same                              | Remove one item or all history.                                                                                    |
| `ClipboardService.Add` (internal)    | same                              | Adds an item, detects sensitive text, starts auto-clear timer, trims to limit.                                     |
| `ClipItem`                           | same                              | Text/image item with display, metadata and sensitivity.                                                            |
| `OcrService.RecognizeAsync`          | `Services/OcrService.cs`          | Windows OCR on a bitmap; joins `OcrResult.Lines` with newlines; null or localized failure on unsupported language. |
| `TextAnalysisService.LooksLikeCode`  | `Services/TextAnalysisService.cs` | Detects code by extension, assignments, calls or code-shaped lines; Markdown paths stay prose.                     |
| `TextAnalysisService.FindCodeTokens` | same                              | Tokenizes keywords/functions, strings, numbers and comments for the OCR editor.                                    |
| `TextAnalysisService.FindColorCodes` | same                              | Finds distinct hex, RGB/RGBA and HSL/HSLA tokens and converts them to `Color` values.                              |
| `CodeToken` / `TextColorCode`        | same                              | Code-token span/type and color-token text/parsed color for the OCR window.                                         |
| `UpdaterService.CurrentVersion`      | `Services/UpdaterService.cs`      | Reads the stamped informational version, falling back to `0.0.100`.                                                |
| `UpdaterService.CheckAsync`          | same                              | GitHub release check and `.exe`/`.msi` asset selection.                                                            |
| `UpdaterService.DownloadAsync`       | same                              | Download asset to temp with progress.                                                                              |
| `UpdaterService.CreateBackup`        | same                              | Copy the current executable and version into the rollback folder.                                                  |
| `UpdaterService.ApplyUpdate`         | same                              | Backup, run a setup with `/S` or copy an executable after exit, then restart.                                      |
| `UpdaterService.RestoreBackup`       | same                              | Restore the previous executable.                                                                                   |
| `UpdaterService.DeleteBackup`        | same                              | Remove rollback backup.                                                                                            |
| `UpdaterService.OpenReleasePage`     | same                              | Open the release page or the releases index.                                                                       |
| `LiveDrawWindow`                     | `Views/LiveDrawWindow.xaml(.cs)`  | Transparent desktop ink overlay; pen/arrow/box/text, fade or persist, undo/clear, and composited annotated-screen PNG. |
| `LiveInkSurface`                     | `Views/LiveInkSurface.cs`         | Draws committed and preview ink items in overlay and capture compositing coordinates.                               |
| `ScreenRulerWindow` / `RulerSurface` | `Views/ScreenRulerWindow.xaml(.cs)`, `Views/RulerSurface.cs` | Physical-pixel horizontal/vertical/diagonal/region measurement, alignment guides, and readout copy. |
| `FileShelfWindow`                    | `Views/FileShelfWindow.xaml(.cs)` | Floating file/folder drop target with drag-out, batch file actions, PDF merge, and path copy.                        |
| `ImageEditorWindow` / `ImageResizeWindow` | `Views/ImageEditorWindow.xaml(.cs)`, `Views/ImageResizeWindow.xaml(.cs)` | Existing-image annotation, crop, rotate, resize, redaction, convert, save, and copy.          |
| `DeveloperToolboxWindow`             | `Views/DeveloperToolboxWindow.xaml(.cs)` | Local JSON/XML formatting, Base64/URL transforms, SHA-256, GUID, and regex testing.                         |

## Workbench services and tests

| Symbol | Location | Purpose |
|---|---|---|
| `CaptureLibraryService.Record` | `Services/CaptureLibraryService.cs` | Stores source app/window, tags and favorites when a capture is saved. |
| `CaptureLibraryService.Search` / `DeleteToRecycleBin` | same | Filters metadata and moves selected capture files to the Recycle Bin. |
| `WorkflowService.RunPostCaptureAsync` | `Services/WorkflowService.cs` | Matches and executes Copy/Save recipes with filename templates and optional resize. |
| `UploadProviderService.UploadImageAsync` | `Services/UploadHistoryService.cs` | Routes an upload to Catbox or the local folder provider. |
| `UploadHistoryService.Add` / `ExpireLocalUploads` | `Services/UploadHistoryService.cs` | Persists links and removes expired local files. |
| `FileActionQueueService.RunAsync` | `Services/FileActionQueueService.cs` | Runs pending/retry/dry-run jobs with overwrite policy and cancellation. |
| `FileToolsService.CreateUniquePath` | `Services/FileToolsService.cs` | Avoids overwriting outputs while preserving the extension. |
| `ColorStudioService.ExtractPalette` / `ExportCss` / `Simulate` | `Services/ColorStudioService.cs` | Palette, token exports, and color-vision simulation. |
| `ScrollingCaptureService.StitchVertical` / `PlaceControlBar` | `Services/ScrollingCaptureService.cs` | Detects row overlap, stitches frames, and places the scroll control bar; `ScrollingCaptureWindow` collects the frames. |
| `PdfToolsService.ParsePageRange` / `ExtractPdfPagesAsync` / `MergePdfsAsync` / `OcrPdfAsync` | `Services/PdfToolsService.cs` | PDF page selection and page-range operations. |
| `DiagnosticsService.Write` / `Exception` | `Services/DiagnosticsService.cs` | Bounded log writes and unhandled-exception records. |
| `TopmostService.ReleaseSavedTopmosts` | `Services/TopmostService.cs` | Safe-mode release of matching saved topmost windows and the session file. |
| `Tests/Program.cs` | `Tests/Program.cs` | Dependency-free assertions for pure services (9 checks). |

## Hotkeys and native interop

| Symbol                                               | Location                          | Purpose                                                                                    |
| ---------------------------------------------------- | --------------------------------- | ------------------------------------------------------------------------------------------ |
| `GlobalHotkeyService.Register`                       | `Services/GlobalHotkeyService.cs` | Registers one id after unregistering its old combination.                                  |
| `GlobalHotkeyService.Unregister`, `UnregisterAll`    | same                              | Removes one or all hotkeys.                                                                |
| `GlobalHotkeyService.IsRegistered`                   | same                              | Used by the PrintScreen banner.                                                            |
| `GlobalHotkeyService.TryCaptureShortcut`             | same                              | Maps WPF key and modifiers to tokens; standalone keys are F1 to F12, PrintScreen and Home. |
| `GlobalHotkeyService.FormatShortcutDisplay`          | same                              | Human-readable shortcut, LTR.                                                              |
| `NativeMethods.GetWindowLongPtr`, `SetWindowLongPtr` | `Services/NativeMethods.cs`       | Extended window style access.                                                              |
| `NativeMethods.SetWindowPos`                         | same                              | Topmost and physical placement.                                                            |
| `NativeMethods.RedrawWindow`                         | same                              | Window repainting and frame invalidation.                                                  |
| `NativeMethods.SendPlainPasteShortcut`               | same                              | `SendInput` Ctrl+V.                                                                        |
| `NativeMethods.PrintWindow`                          | same                              | Window capture.                                                                            |
| `NativeMethods.RegisterHotKey`, `UnregisterHotKey`   | same                              | Hotkey registration.                                                                       |
| `NativeMethods.GetDpiForMonitor`                     | same                              | Per-monitor scale.                                                                         |
| `NativeMethods.StrCmpLogicalW`                       | same                              | Natural Windows Explorer filename sorting.                                                 |
| `NativeMethods.NotifyShellAssociationChanged`        | same                              | Broadcasts `SHChangeNotify(SHCNE_ASSOCCHANGED)` to refresh Explorer context menus.         |

## UI components and handlers

| Component | Key behavior |
|---|---|
| `MainWindow.xaml` | `1040x710` Cryo-Family frame (`WindowBorderBrush` 1 px); 36 px title bar with `TxtHeaderHotkey`, Capture/Record launchers, `BtnQuit`, `BtnClose`; horizontal underline `GroupName="MainNav"` tab strip; Overview Hero Command Banner, 4 unclipped Quick Launch tiles, Recent Captures strip, 2x3 Domain-Colored Module grid; 2-column Capture panel; Compressor + combined Color/Contrast studio + QR Suite + Pinner/Clipboard tools; Hotkeys table; zero-scroll 2-column Settings & Updates panel; status bar footer. |
| `MainWindow.xaml.cs` | `ReloadUi`, `ShowTab`, `MainContentScroll_PreviewMouseWheel`, `BtnQuit_Click`, `ResetQuitConfirmState`, `ModuleToggle_Changed`, `Setting_Changed`, `CmbTheme_SelectionChanged`, `CmbLanguage_SelectionChanged`, `RefreshHotkeyRows`, `AddHotkeyRow`, `RefreshRecentStrip`, `LoadCompressionSource`, `BtnCompress_Click`, `RefreshPinnedState`, `RefreshClipboardState`, `BtnExportSettings_Click`, `BtnImportSettings_Click`, `BtnResetSettings_Click`, `BtnHeroCapture_Click`, `BtnRecordNow_Click`, `BtnGoCompress_Click`, `BtnGoQr_Click`, update handlers, `TitleBar_MouseLeftButtonDown`, `BtnClose_Click`, `Window_Closing` (hide to tray unless shutdown is in progress). |
| `CaptureOverlayWindow.xaml` | Selection canvas, top-left size badge, optional magnifier, vertical tool rail, horizontal action rail, tooltips. |
| `CaptureOverlayWindow.xaml.cs` | Selection drag/move/resize, handles, double-click window, Ctrl+A, annotation tool selection, color/width, undo/redo, text editor, blur commit, OCR/pin/print/save/copy shortcuts, Esc; places both rails without overlap and gates the magnifier on `ShowCaptureLoupe`. |
| `CaptureSurface.cs` | Shared physical selection, annotation list, hit testing, cursor state, redraw. |
| `HotkeyCaptureWindow.xaml` | Modal key capture with modifier display and validation. |
| `OcrResultWindow.xaml` | Auto-height, top-aligned rich OCR editor; code/wrap toggles, syntax colors, line/character metadata, color swatches, Open/Save as, drag/drop for Markdown and all text types. |
| `ClipboardHistoryWindow.xaml` | Searchable list, named snippets, copy/remove/clear, image OCR, transforms, optional encrypted persistence and expiry. |
| `ClipboardTransformWindow.xaml` | Preview a selected text transform before copying. |
| `PowerToolsWindow.xaml` | Cryo Workbench: Library, Recipes, Privacy, File Actions, Colors, Pinner, PDF/Media, Diagnostics. |
| `QrResultWindow` | `Views/QrResultWindow.xaml(.cs)` | Borderless Cryo-Family popup (`TitleBar_MouseLeftButtonDown` drag + close) showing decoded QR payload, format, clickable QR thumbnail, copy text, save text, and save QR image. |
| `PinWindow.xaml` | Frameless always-on-top image, drag, wheel zoom, Ctrl+wheel opacity, right-click menu. |
| `UploadProgressWindow.xaml` | Non-topmost, taskbar-minimizable cloud upload progress popup with real-time byte percentage, cancel on close, auto-copy link on completion, and inline Copy icon button. |
| `OperationProgressWindow.xaml` | Reusable cancelable progress popup used by video-to-GIF and folder-to-GIF conversions. |
| `ToastWindow.xaml` | Non-intrusive toast, bottom-right, click-through. |
| `TrayIconManager.cs` | Packaged CryoUtils icon with custom `DarkMenuRenderer`, rounded pill hover, custom anti-aliased chevrons, floor width constraints, full RTL support, capture, delayed capture submenu, record, color picker, QR suite submenu, clipboard history, dynamic pin/unpin/reset items, dashboard, settings, and exit. |
| `RecentCaptureService.cs` | Manages `Items` (`Session`) and `AllTimeItems` (`EffectiveSaveFolder`), background disk thumbnail loading with cancellation (`ReloadAllTimeAsync()`), inline delete confirmation, `NotifyFilesDeleted()`, and dimension/filesize formatting. |
| `CryoIcon` / `IconData` | Vector geometry resources used by XAML (`Home`, `Keyboard`, `Info`, `Edit`, `Tools`, `Record`, `Menu`, `Minimize`, `Check`, `Folder`, `Trash`, `Capture`, `Droplet`, `Pin`, `Compress`, `Clipboard`, `Settings`, `Close`, `Power`, `QrCode`, `Contrast`, `CloudUpload`). |

## Reuse rules

- Use `ImageIO` for every encode, decode, resize and byte-size format.
- Use `ScreenCaptureService` for every physical-pixel capture, crop and clamp.
- Use `AnnotationRenderer` for every annotation export.
- Use `ThemeService.GetBrush` and DynamicResource tokens only. Do not hardcode colors.
- Use `LocalizationService.Get` and `Format` for every visible string.
- Use `TextAnalysisService` for OCR code detection, syntax tokenization and color-code parsing instead of duplicating regexes in a window.
- Use `SettingsService` for settings; dedicated services own their documented metadata stores (`capture-library.json`, `workflows.json`, `upload-history.json`) instead of overloading settings.
- Use `GlobalHotkeyService` for every global shortcut; do not create a second hotkey window.
- Use `RecordingBarWindow` and `RecorderService` for every recording path.
- Use `ClipboardService` for history, plain paste and clip restoration.
