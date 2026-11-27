# HANDOFF - CryoUtils

**Last updated:** 2026-11-27 19:18

## Status
Version bumped to `0.0.102` with [ReleaseNotes/v0.0.102.md](ReleaseNotes/v0.0.102.md). Release and Debug builds clean (`0 Warning(s)`, `0 Error(s)`). `CHECKS=43 FAILURES=0`. Localization parity `EN=1014 AR=1014 DIFF=0`. Live dashboard launch verified cleanly.

## Completed This Session
- **Bug Fixes & BugReport Bezel**: Removed distracting `CaptureFlashWindow` flash overlay on copy/save/snap; added `SourceApp · yyyy-MM-dd HH:mm` bottom bezel stamp to `ApplyPrettyFrame` when `BugReport` profile is active; fixed negative multi-monitor `VirtualBounds` coordinate offset in `AnnotationRenderer` (`Blur`, `Zoom`, `Spotlight`); compacted vertical `AnnotationToolbar` button height (`30 -> 26`) so rails fit tight viewports cleanly.
- **Deeper RAM Cuts**: Compressed unsaved `RecentCapture` bitmaps as PNG (`CompressedPng` + `ResolveFullImage()`), added `trimWorkingSet: true` on 90s overlay pool expiry, cleared `CaptureLibraryItem.Thumbnail` refs on `WorkbenchTabView.ReleaseResources()`, and replaced GPU `DropShadowEffect` on `ApertureTile` with zero-shader `ApertureHaloRing`.
- **Visual & UX Polish**: Added `90ms` click-through `CaptureFlashWindow` on copy/save/snap, toggleable compact horizontal mini-row mode for Recent captures (`RecentCompactRows`, `BtnRecentLayout`), and inline 4-color swatch dots (`#FF4F5E`, `#2B88D8`, `#F5B731`, `#26A269`) on `AnnotationToolbar`.
- **Recent Quick Copy**: Added `ToBase64DataUri(byte[], mime)` and Recent card/row context menu (`Copy file path`, `Copy Markdown`, `Copy Base64 URI`).
- **Crash Fix (`FilterPillButton`)**: Added missing `FilterPillButton` `ToggleButton` style to [Resources/Controls.xaml](Resources/Controls.xaml) so `MainWindow.xaml` (`LiteQuickBar` pills) loads without throwing `XamlParseException`.
- **Compact Dashboard Size**: Reduced default dashboard footprint from `680x700` (`MinWidth=560`) to `480x450` in Lite mode and `480x565` in Full Suite (`MinWidth=430`, `MinHeight=360`), tightened Hero/TitleBar/Tabs/Module row spacing in [MainWindow.xaml](Views/MainWindow.xaml) and [Controls.xaml](Resources/Controls.xaml), and gated saved size persistence behind `AppSettings.WindowSizeCustomized`.
- **RAM Optimizations**:
  - `Services/ClipboardService.cs` & `Views/ClipboardHistoryWindow.xaml.cs`: `ClipItem` now stores frozen `Thumbnail` (<=180px) + PNG `CompressedPng` bytes and decodes full bitmaps on demand via `ResolveFullImage()`.
  - `Services/RecorderService.cs`, `Services/GifEncoder.cs`, `Services/Mp4Writer.cs`, `Views/RecordingBarWindow.xaml.cs`: GIF recording frames now spool directly to temp disk PNGs (`FramePaths`, `RecordingResult : IDisposable`) instead of buffering up to 380 MB in RAM; `GifWriter.EncodePngFilesToGif` and `Mp4Writer.Write` read streamed frame files from disk.
  - `Views/CaptureController.cs`: Added 90s idle `_poolIdleTimer` disposing parked `_overlayPool` HWNDs when inactive.
  - `Views/MainWindow.xaml.cs`: Added 30s `_inactiveTabTimer` releasing `_toolsTab` / `_workbenchTab` visuals when staying on Overview/Capture/Hotkeys/Settings.
- **Visual Polish**:
  - `Services/RecentCaptureService.cs`: Extracts frame-0 thumbnails for `.mp4` captures via `MediaConversionService.SaveThumbnailAsync` with placeholder fallback.
  - `Services/ThemeService.cs`, `Models/AppSettings.cs`, `Views/Tabs/GeneralTabView.xaml(.cs)`, `Views/MainWindow.xaml`: Added `AccentPreset` selector (`CryoBlue`, `Cyan`, `Emerald`, `Violet`, `Amber`, `Rose`) driving `AccentBrush`, toggle tracks, and frozen `HeroBackgroundGradientBrush`.
  - `Views/PinWindow.xaml(.cs)`: Added hover `MiniHudBar` with 90ms fade, Opacity `-/+`, Ghost toggle, Copy, and Close controls (hidden when click-through).
  - `Views/MainWindow.xaml(.cs)` & `App.xaml.cs`: Added 90ms tray-anchor reveal animation (`PlayTrayRevealAnimation`) on initial dashboard open (disabled on capture restore).
- **New Features**:
  - **Zoom Callout (`M`)**: Added `CaptureTool.Zoom` / `AnnotationKind.Zoom` 2.0x circular zoom lens in `AnnotationRenderer.DrawZoom`, `CryoIcons.ZoomLoupe`, and `CaptureOverlayWindow`.
  - **Auto-Redact**: Added WinRT word-box extraction (`WinRtBridge.RecognizeOcrWordsAsync`), `OcrService.FindSensitiveBounds` (emails, API/JWT/Bearer tokens, cards, phone numbers), `CaptureController.AutoRedactSelectionAsync` undoable blur rects, and overlay action button (`CryoIcons.RedactAuto`).
  - **Quick Capture Profiles**: Non-destructive `ActiveCaptureProfile` (`Default`, `Discord`, `BugReport`, `OcrOnly`) wired into `CaptureController.ResolveCaptureProfile`, Hero pill (`BtnHeroProfile`), tray `Capture profile` submenu (`TrayIconManager`), and `DashboardSearchService`.
- **Verification**: Added 6 new unit tests in `Tests/Program.cs` (39 checks total) and verified EN/AR parity (`1004 = 1004`).

## Pending
- Verify final `CHECKS=39 FAILURES=0` and inspect clean Release run; commit when user approves.
