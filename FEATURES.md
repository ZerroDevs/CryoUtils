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
| Highlighter | Y | Semi-transparent wide stroke. |
| Inline text EN/AR | Y | Editable overlay text box with RTL flag. |
| Irreversible blur / pixelate | Y | Pixelated into the export bitmap. |
| Auto-increment step badges | Y | Circular numbered callouts. |
| Color swatches and custom color | Y | Custom picker plus recent palette. |
| Thickness with wheel | Y | Live size preview. |
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
| OCR preview | V | Compact auto-height window; one line rendered at 640 x 250 with a 72 px text box. |
| Editable OCR text | Y | Text starts at the top, can be edited, and scrolls when long. |
| OCR code mode | V | C# sample rendered in mono with no-wrap horizontal scrolling. |
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
| Text and image clips | Y | In-memory list. |
| Search | Y | Filter in history window. |
| Copy item back | Y | Text or image. |
| Remove one / clear all | Y | History window and Tools card. |
| Sensitive auto-clear | Y | URL, token, password-like heuristic; timer. |
| History limit | Y | Clamped 10 to 100. |
| Plain-text paste | Y | Strips formatting, puts text, sends Ctrl+V. |
| Module off destroys listener | Y | `SetEnabled(false)` disposes the hidden source. |

## 6. Dashboard

| Feature | Status | Notes |
|---|---|---|
| Custom dark window chrome | Y | Title bar drag and hide/quit buttons. |
| Tabs: Overview / Capture / Tools / Hotkeys / General / About | Y | Radio navigation. |
| Module card grid | Y | Five modules with toggles, hotkey pill, open action. |
| Active module count | Y | Localized summary. |
| Recent captures strip | Y | Thumbnails, copy, compress, open folder, delete. |
| Empty recent state | Y | Visible when no captures. |
| Quick capture and record actions | Y | Hero and Overview buttons. |
| Status and version | Y | `v0.0.100` from `VERSION`. |
| Focus indicators | Y | Cryo styles, keyboard tabs. |
| Escape hides dashboard | Y | Unless a text box has focus. |

## 7. Tray

| Feature | Status | Notes |
|---|---|---|
| Packaged CryoUtils icon | U | New `CryoUtils-Icon.png` rendered into a multi-size ICO and embedded in the executable/window/installer. A tightly cropped `CryoUtils-Tray.ico` fixes the oversized transparent border, and the tray loads the current 16/20/24/32 px slot size. Tray rendering was previewed at 16/20/24 px but not inspected live in the notification area. |
| Left-click configurable | Y | Dashboard or capture. |
| Right-click menu | Y | Capture, record, color picker, dashboard, settings, exit. |
| Dark menu renderer | Y | Cryo surfaces, teal highlight. |
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
| Run in tray when closed | Y | On by default; close hides. Turning it off makes close exit. |
| Window position restore | Y | Bounds checked against virtual screen. |
| Theme Dark / Light / System | V | Live switch tested. |
| Language English / العربية | V | Live switch tested. |
| RTL layout | Y | Window flow direction; technical values LTR. |
| Localization parity | V | 298 keys each, no duplicates, placeholders match. |

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
| Per-user Inno Setup installer | V | Compiled to `Installer\Output\CryoUtils-Setup-0.0.100.exe`; installs under `%LOCALAPPDATA%\Programs\CryoUtils` with no elevation. |
| Portable self-contained zip | V | Built at `release\CryoUtils-0.0.100-win-x64.zip`; contains app, `VERSION`, `LICENSE`, and `NOTICE`, without PDBs. |
| Local installer build script | V | `build-installer.ps1` publishes, compiles, and writes SHA-256 files. |
| GitHub release workflow | U | Added for Windows builds, installer, optional signing, checksums, and `v<version>` release; not run on GitHub. |
| Optional Authenticode signing | U | Wired to the Comile-compatible secret names; unsigned builds are valid and were produced here. |
| Installer execution and uninstall | U | Installer was compiled but not run on this machine. |
| Version baseline | V | `0.0.100` stamped into the app, setup, and published payload. |

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
| Offline privacy | Y | No analytics, no cloud, clipboard memory only, update check optional. |
