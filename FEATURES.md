# CryoUtils Feature Catalog

Status key: `Y` implemented, `V` implemented and verified in this environment, `U` implemented but not fully verified.

## 1. Hero capture module

| Feature | Status | Notes |
|---|---|---|
| Global capture hotkey | Y | Default `PrintScreen`; configurable; fallback suggestion `Ctrl+Shift+S`. |
| Standalone HOME hotkey | Y | HOME works alone or with modifiers; hint updated in both languages. |
| Contested PrintScreen guidance | Y | Dashboard banner when registration fails; one-click fallback. |
| Multi-monitor freeze frame | V | 2 overlays created on 2 monitors. |
| PerMonitorV2 DPI awareness | Y | Manifest and monitor DPI query. Mixed scaling not tested. |
| Dimmed background and crosshair | Y | Overlay per monitor. |
| Magnifier loupe | Y | Live zoom grid near cursor. |
| Drag selection with live size badge | V | 240 x 140 export matched drag. |
| Move selection | Y | Drag inside selection. |
| Eight resize handles | Y | Corners and edges. |
| Double-click window selection | Y | Uses window under cursor. |
| Ctrl+A full monitor selection | Y | Fills the overlay monitor. |
| Vertical annotation rail | Y | Flips inward near screen edge. |
| Pen | Y | Polyline with smoothing. |
| Line with Shift snap | Y | 45 / 90 degree snapping. |
| Arrow | Y | Scaled arrowhead. |
| Rectangle with Shift square | Y | Hollow outline. |
| Ellipse / spotlight | Y | Outline ellipse and dimming spotlight. |
| Redaction brush | Y | Opaque black freehand stroke, rendered into the export. |
| Move or delete annotations | Y | Click an existing annotation in the overlay, drag to move, Delete to remove. |
| Crop / rotate 90° | Y | Reopens the finished result as a new editable capture. |
| Scrolling capture | U | Automatic frames plus overlap stitching; the control bar hides during each frame; sticky headers can still artifact. |
| Highlighter | Y | Semi-transparent wide stroke. |
| Inline text EN/AR | Y | Editable overlay text box with RTL flag. |
| Irreversible blur / pixelate | Y | Pixelated into the export bitmap. |
| Auto-increment step badges | Y | Circular numbered callouts. |
| Color swatches and custom color | Y | Custom picker plus recent palette. |
| Thickness with wheel | Y | Live size preview. |
| Action/tool rail collision avoidance | Y | Small selections place the action rail in a free slot around the tool rail. |
| Optional pixel magnifier | Y | Off by default; enable in Capture settings. When on, avoids both rails. |
| Undo / redo | Y | Vector object stack, Ctrl+Z / Ctrl+Y. |
| Copy to clipboard | V | Ctrl+C produced exact 240 x 140 image. |
| Save PNG / JPG / WebP | Y | Save dialog or quick save with timestamp. |
| Record GIF / MP4 from selection | V | Recording bar appeared; GIF/MP4 encoders tested separately. |
| Recording timer, FPS, format | Y | 15 / 24 / 30 FPS, GIF / MP4. |
| Estimated output size | Y | Approximate live estimate. |
| Pause / resume | Y | Capture thread pauses. |
| Stop and save | Y | Save dialog, format-specific encode. |
| Stop and copy | Y | Copies the encoded file as a file drop. |
| Cancel recording | Y | Drops frames and closes bar. |
| Red recording border | Y | Click-through frame outside the region. |
| Offline OCR | V | `HELLO CRYOUTILS 123` recognized. |
| OCR preview | V | Compact auto-height window; preserves the OCR line structure. |
| Editable OCR text | Y | Rich editable text starts at the top, scrolls when long, and re-highlights while typing. |
| OCR code mode | V | Syntax-colored keywords/functions, strings, numbers and comments with no-wrap horizontal scrolling. |
| OCR color swatches | V | Hex, RGB/RGBA and HSL/HSLA swatches with click-to-copy. |
| OCR file open / save / drop | U | Markdown and code loaders verified; dialogs and real drag/drop were not clicked. |
| OCR Markdown source | V | `sample.md` loads as wrapped editable text, without markdown rendering. |
| Pin to screen | Y | Frameless topmost image. |
| Pin zoom / opacity | Y | Wheel and Ctrl+Wheel. |
| Print | Y | Ctrl+P. |
| Escape closes | V | Overlay closed and dashboard restored. |

## 2. Color picker module

| Feature | Status | Notes |
|---|---|---|
| Enabled by default | Y | Module card and hotkey. |
| Global hotkey | V | Ctrl+Shift+C opened the picker. |
| Full-screen freeze frame | Y | Physical-pixel snapshot. |
| Cursor magnifier grid | Y | Pixel zoom near cursor. |
| HEX / RGB / HSL | V | Window exposes all three values. |
| Left-click copies selected format | Y | Format preference persists. |
| Recent 10 colors | Y | Stored in settings, shown in Tools. |
| Esc closes | V | Window closed and dashboard restored. |
| Disable unregisters hotkey | V | No picker while off; on restored. |

## 3. Window pinner module

| Feature | Status | Notes |
|---|---|---|
| Enabled by default | Y | Module card and hotkey. |
| Global hotkey | Y | Ctrl+Win+T. |
| Toggle `WS_EX_TOPMOST` | V | Harness read the style before and after. |
| Click-through 2 px border | Y | `PinnedBorderWindow`, option in General. |
| Confirmation sound | Y | Optional, off by default. |
| Unpin from dashboard | Y | Tools card. |
| Restore on exit | Y | `TopmostService.RestoreForExit`. |
| Multiple pinned windows | Y | Independent pin states; dashboard and Workbench list them. |
| Per-window opacity / click-through | Y | Workbench Pinner tab; 20–100% opacity. |
| Exclusion list | Y | Process names skipped by the pinner. |
| Restore after restart | U | Matches saved title and process; renamed/closed windows do not restore. |

## 4. Compressor module

| Feature | Status | Notes |
|---|---|---|
| Dashboard drop zone | Y | Drag file or browse. |
| PNG / JPG / JPEG / WEBP / GIF | V | All accepted by service. |
| Discord 10 MB preset | Y | Quality 88, scale 100. |
| WhatsApp / light preset | Y | Quality 70, scale 80. |
| Custom quality and scale | Y | Sliders enabled only in Custom. |
| Live preview | V | Decoded preview returned for each format. |
| Before / after size and percent | Y | Uses formatted bytes and saved percent. |
| Keep original if encode is larger | V | PNG and GIF returned source bytes. |
| Save As | Y | File dialog with result extension. |
| Copy File | Y | File drop list to clipboard. |
| Module off disables UI | Y | Sliders and buttons disabled. |

## 5. Clipboard and plain paste module

| Feature | Status | Notes |
|---|---|---|
| Disabled by default | Y | Opt-in. |
| Global history hotkey | Y | Ctrl+Shift+H. |
| Clipboard listener | V | Captured a new text item. |
| Text and image clips | Y | In-memory list by default; text-only DPAPI persistence is opt-in. |
| Search | Y | Filter in history window. |
| Copy item back | Y | Text or image. |
| Remove one / clear all | Y | History window and Tools card. |
| Sensitive auto-clear | Y | URL, token, password-like heuristic; timer. |
| History limit | Y | Clamped 10 to 100. |
| Named snippets | Y | Add, copy, and remove reusable clips. |
| Text transforms | Y | Upper/lower/title/snake/kebab/camel, URL encode/decode, Base64, JSON. |
| Transform preview | Y | `ClipboardTransformWindow` before copying. |
| Image OCR | Y | Runs Windows OCR on an image clip. |
| Per-item expiry | Y | Optional age-based cleanup for persisted text. |
| Plain-text paste | Y | Strips formatting, puts text, sends Ctrl+V. |
| Module off destroys listener | Y | `SetEnabled(false)` disposes the hidden source. |

## 6. Dashboard

| Feature | Status | Notes |
|---|---|---|
| Custom dark window chrome | Y | Title bar drag and hide/quit buttons. |
| Tabs: Overview / Screen Capture / Tools & Utilities / Hotkeys / Settings | Y | Radio navigation; About and Updates are integrated into Settings. |
| Module card grid | Y | Five modules with toggles, hotkey pill, open action. |
| Active module count | Y | Localized summary. |
| Recent captures strip | Y | Thumbnails, copy, compress, open folder, delete. |
| Activity Center | U | Service-fed cards and quick actions above the recent captures strip. |
| Empty recent state | Y | Visible when no captures. |
| Quick capture and record actions | Y | Hero and Overview buttons. |
| Status and version | Y | `v0.0.101` from `VERSION`. |
| Focus indicators | Y | Cryo styles, keyboard tabs. |
| Escape hides dashboard | Y | Unless a text box has focus. |

## 6.1 Workspace tools and Activity Center

Status is `U` for these surfaces because they compile and are wired, but the modeless overlays and drag/drop were not exercised in this environment.

| Feature | Status | Notes |
|---|---|---|
| Activity Center on Overview | U | Recent captures/uploads, workflow runs/failures, active file jobs, pinned windows, storage/retention, and quick actions. |
| Live Draw hotkey | U | Configurable hotkey, default `Ctrl+Shift+D`; transparent virtual-screen ink layer without freeze-frame capture. |
| Live Draw tools | U | Pen, arrow, box, text; fade after 5 seconds or persist; undo, clear, and composited capture. |
| Screen Ruler | U | Horizontal, vertical, diagonal, and region measurements; angle/readout copy and click-to-place guides. |
| File Shelf | U | Floating drop target, drag-out, batch compress/convert, PDF merge, path copy, and clear. |
| Image Editor | U | Opens PNG/JPG/WebP/BMP; annotate, crop, rotate, resize, redact, save/convert, copy. |
| Developer Toolbox | U | JSON/XML, Base64/URL, SHA-256, GUID, and regex transforms locally. |

## 7. Tray

| Feature | Status | Notes |
|---|---|---|
| Packaged CryoUtils icon | U | New `CryoUtils-Icon.png` rendered into a multi-size ICO and embedded in the executable/window/installer. A tightly cropped `CryoUtils-Tray.ico` fixes the oversized transparent border, and the tray loads the current 16/20/24/32 px slot size. Tray rendering was previewed at 16/20/24 px but not inspected live in the notification area. |
| Left-click configurable | Y | Dashboard or capture. |
| Right-click menu | Y | Capture, record, color picker, dashboard, settings, exit. |
| Dark menu renderer | Y | Cryo surfaces, Cryo Blue highlight. |
| Language-aware menu | Y | Labels and RTL rebuild on a language switch. |
| Runs with no window | V | Default launch and `--background` produced zero visible windows; tray icon was visible as `CryoUtils`. |
| Exit cleanup | Y | Unregisters hotkeys and disposes services. |

## 8. Settings and persistence

| Feature | Status | Notes |
|---|---|---|
| JSON settings file | Y | `%APPDATA%\CryoUtils\settings.json`. |
| Atomic writes | Y | Temp file then move. |
| Export | Y | Save dialog. |
| Import | Y | Confirmation, apply, reload. |
| Reset | Y | Confirmation and defaults. |
| Run on startup | Y | Off by default; HKCU Run entry, no admin. |
| Run in background | Y | On by default; manual launch starts in the tray. |
| X button hides to tray | Y | Minimize button removed; X and Alt+F4 hide the dashboard. Quit only from the tray menu. |
| Window position restore | Y | Bounds checked against virtual screen. |
| Safe capture retention | Y | Only `CryoUtils_*` files; pruned items go to the Recycle Bin; lowering the limit asks first. |
| Copied/upload archive toggle | Y | `ArchiveCopiedCaptures` controls Ctrl+C and upload auto-archiving; explicit saves still persist. |
| Cloud upload consent | Y | First upload explains catbox.moe; Settings can disable uploads entirely. |
| Async all-time gallery | Y | Thumbnails load off the UI thread with cancellation and cache reuse. |
| Theme Dark / Light / System | V | Live switch tested. |
| Language English / العربية | V | Live switch tested. |
| RTL layout | Y | Window flow direction; technical values LTR. |
| Localization parity | V | 696 keys each, no duplicates, placeholders match. |

## 9. Updater

| Feature | Status | Notes |
|---|---|---|
| GitHub API check | Y | `ZerroDevs/CryoUtils`. |
| Semantic version parse | Y | Numeric tag comparison. |
| Asset selection | Y | Prefers an `.exe` setup asset; portable zip is a manual download. |
| Download with progress | Y | Dashboard progress text. |
| Apply after exit | Y | Copy, backup, delayed cmd script, restart. |
| Rollback backup | Y | `.bak` plus script. |
| Restore previous version | Y | About tab. |
| Delete backup | Y | About tab. |
| Automatic check | Y | On startup if enabled and due. |
| Missing asset status | Y | Explicit localized status. |
| Real release round trip | U | No published release to test. |

## 10. Packaging and release

| Feature | Status | Notes |
|---|---|---|
| Per-user Inno Setup installer (`Full` + `Lite`) | V | Builds `CryoUtils-Setup-0.0.101.exe` (self-contained) and `CryoUtils-Setup-Lite-0.0.101.exe` (framework-dependent with auto `.NET 10 Desktop Runtime` install); installs under `%LOCALAPPDATA%\Programs\CryoUtils` with no elevation and NTFS post-install `compact.exe` compression. |
| Portable zips (`Full` + `Lite`) | V | Builds `release\CryoUtils-0.0.101-win-x64.zip` and `release\CryoUtils-Lite-0.0.101-win-x64.zip` containing app, `VERSION`, `LICENSE`, and `NOTICE`, without PDBs. |
| Local installer build script | V | `build-installer.ps1 -Variant All\|Full\|Lite` publishes, compiles, and writes SHA-256 files. |
| GitHub release workflow | V | Runs on Windows builds, compiles Full & Lite installers + zips, supports optional signing, checksums, and `ReleaseNotes/v<version>.md` for `v<version>`. |
| Optional Authenticode signing | U | Wired to the Comile-compatible secret names; unsigned builds are valid and published with SHA-256 verification. |
| Edition-matched updater | V | `UpdaterService` detects `coreclr.dll` to select Full (`CryoUtils-Setup-*`) vs Lite (`CryoUtils-Setup-Lite-*`). |
| Version baseline | V | `0.0.101` stamped into the app, setup, and published payload. |

## 11. Quality and accessibility

| Item | Status | Notes |
|---|---|---|
| `asInvoker` manifest | V | Read from embedded exe manifest. |
| No UAC prompt | V | Standard-user launch. |
| Build clean | V | Release build into `build-check` had 0 warnings and 0 errors. |
| No admin dependency | Y | Registry write is HKCU. |
| Keyboard tab order | Y | Standard WPF controls, focus styles. |
| Accessible names | Y | AutomationProperties on icon buttons and fields. |
| Contrast | Y | Key text token pairs measured at or above 4.5:1. A full screen-by-screen contrast audit was not run. |
| Empty / busy / error states | Y | Recent empty, compression busy, error toasts. |
| No hardcoded visible UI strings | Y | Localization dictionaries; technical tokens remain literal. |
| Privacy controls | Y | No analytics; optional GitHub updates; explicit consent and a Settings toggle for Catbox uploads; clipboard memory-only by default with optional DPAPI text persistence. |
| Service test runner | V | `Tests/CryoUtils.Tests.csproj`: 9 focused checks, 0 failures. |

## 12. Cryo Workbench

| Feature | Status | Notes |
|---|---|---|
| Capture library metadata | Y | Source app/window, tags, favorites, search; `capture-library.json`. |
| Bulk library actions | Y | Open, reveal, favorite, tag, and Recycle Bin deletion. |
| Post-capture recipes | Y | Runs after Copy/Save; app filter, filename template, resize, save/copy/OCR/upload. |
| Privacy center | Y | Provider choice, consent status/withdrawal, local-only mode, upload history. |
| Local upload provider | Y | `%APPDATA%\CryoUtils\uploads`; no network; expiry. |
| Catbox remote deletion | Y | Explicitly unavailable; UI says so. |
| File action queue | Y | Convert/compress/recycle, dry run, overwrite policies, cancel/retry. |
| Recycle confirmation | Y | Real (non-dry-run) queue asks before moving files. |
| Color studio | Y | Palette extraction, saved contrast pairs, CSS/JSON/Tailwind exports, CVD preview. |
| PDF page work / OCR | Y | Range extract, merge, split, reorder, rotate, OCR. |
| PDF password encryption | Y | Not implemented by design; UI states the limitation. |
| Video trim / GIF range | Y | Media Foundation video trim; GIF start/end frame range. |
| Clipboard workbench | Y | Snippets, filters, transforms, preview, image OCR. |
| Optional clipboard persistence | Y | Text-only DPAPI; images never persisted. |
| Diagnostics | Y | Bounded logs, exception logging, viewer, safe mode; `--safe-mode` CLI. |
| Workbench UI runtime | U | Built cleanly; no interactive click-through in this environment. |
