# CryoUtils Handoff

Last updated: 2026-11-24

## Latest turn — installer, release, and version 0.0.100

- Added `Installer/CryoUtils.iss`, a per-user Inno Setup definition for `%LOCALAPPDATA%\Programs\CryoUtils` with `PrivilegesRequired=lowest`, `ArchitecturesAllowed=x64compatible`, Start Menu/optional desktop shortcuts, silent-update support, and a running-process stop before install/uninstall.
- Added `build-installer.ps1`: reads `VERSION`, publishes the self-contained win-x64 payload, locates or installs Inno Setup 6.3+, compiles `Installer\Output\CryoUtils-Setup-<version>.exe`, and creates the portable zip plus SHA-256 files.
- Added `.github/workflows/release.yml`: validates `VERSION`, builds and publishes win-x64, compiles the Inno installer, applies optional Authenticode signing from `WINDOWS_SIGNING_PFX_BASE64` / `WINDOWS_SIGNING_PFX_PASSWORD`, creates setup/zip checksums, and publishes the `v<version>` GitHub release.
- Added `LICENSE` (Apache-2.0, copied from the reference project), an adapted `NOTICE`, `README.md`, `SECURITY.md`, `CONTRIBUTING.md`, and release-output entries in `.gitignore`.
- Bumped `VERSION` to `0.0.100`; aligned `app.manifest` to `0.0.100.0` and the updater fallback to `0.0.100`.
- Updated `CryoUtils.csproj` to copy `LICENSE` and `NOTICE` to output/publish alongside `VERSION`.
- Replaced the placeholder app icon with `Images/CryoUtils-Icon.png`, generated a multi-size `Images/CryoUtils.ico` from it, and switched the tray icon to the packaged icon with the old vector mark retained as a fallback.
- Fixed the tray icon's tiny appearance: the source artwork had a large transparent border, so `Images/CryoUtils-Tray.ico` is tightly cropped to the visible mark and `TrayIconManager` now selects the actual notification-area size (16–48 px) instead of always loading a padded 32 px canvas.
- Expanded `.gitignore` for build/debug output, IDE/agent state, local environment files, secrets, and temporary archives.

## Previous turn — OCR

- Reworked the OCR result window (`Views/OcrResultWindow.xaml`): auto-height compact layout, top-aligned editable text, no fixed 470 px empty box.
- Added code mode, wrap toggle, line/character metadata, syntax-neutral monospace display and horizontal scrolling for code.
- Added color-code detection for hex, RGB/RGBA and HSL/HSLA, with clickable color swatches.
- Added open, save-as and file drag/drop for Markdown, source, data and any text file.
- Fixed the shared `CryoTextBox` template so its content host follows `VerticalContentAlignment` (default remains centered for single-line fields).
- Added EN/AR strings for the new OCR controls; localization parity is now 298 keys each.

## Previous turn completed — OCR

- Added `Services/TextAnalysisService.cs`: code detection and hex/rgb/rgba/hsl/hsla parsing.
- Reworked the OCR result window with auto height, top-aligned editing, code mode, wrap, metadata, color swatches, open/save and drag/drop.
- Verified the small-text layout in a WPF harness: 640 x 250 window with a 72 px text box for one OCR line, instead of the old stretched preview.
- Verified code layout (6-line C# sample, 640 x 408, 153 px text box), Markdown load (`sample.md`, normal font, code off, wrap on), `.cs` load (code mode on), and Arabic RTL text.
- Verified color parsing for `#2FD3BD`, `#fff`, `#11223344`, `rgb(255,0,0)`, `rgba(0,255,0,0.5)` and `hsl(240,100%,50%)`.
- Captured dark, light and Arabic layouts for visual inspection; light swatch borders and RTL layout render correctly.
- Added a non-activating red recording frame around the selected region while the recorder is running.
- Fixed the MP4 writer by flattening the `IMFAttributes` vtable into `IMFSample`; a 2 frame test now writes `MP4_OK`.
- Kept the original file when compression cannot beat the source size.
- Added dashboard title bar dragging.
- Added an explicit no-asset status for GitHub releases without an installer.
- Manual launch now defaults to tray-only. `--background` always starts tray-only; set Run in background off to open the dashboard on launch.
- Removed unused `CloseToTray` and duplicated `TrayLeftClickCapture` settings.

## Current state

- Version baseline is `0.0.100`; the next patch is `0.0.101`.
- Release build into a separate verification folder passes with 0 warnings and 0 errors. The normal `bin` build was blocked because a debug `CryoUtils.exe` process (PID 34972) was already running and holding the executable; that process was not killed.
- `build-installer.ps1` completed successfully with the tray icon fix: `Installer\Output\CryoUtils-Setup-0.0.100.exe` (54.22 MB) and `release\CryoUtils-0.0.100-win-x64.zip` (76.87 MB), each with a `.sha256` file.
- Installer and published executable version metadata show `0.0.100`; the portable archive contains `VERSION`, `LICENSE`, and `NOTICE` and no `.pdb` files.
- The installer was not run on this machine, the GitHub workflow was not executed against GitHub, and the new tray icon was not inspected live in the notification area. Its 16/20/24 px renders were previewed on the tray's dark background.
- The executable icon was extracted from the built `CryoUtils.exe` and inspected at 32 x 32; it matches the new CryoUtils mark.
- Manual launch starts in the tray. With Run in background off, the dashboard opens titled `CryoUtils`.
- Background launch (`CryoUtils.exe --background`) creates no visible windows and stays alive.
- Embedded manifest: `requestedExecutionLevel level="asInvoker" uiAccess="false"`, `PerMonitorV2, PerMonitor`.
- No open ports. No background services. No admin requirement.
- App data: `%APPDATA%\CryoUtils\settings.json` (deleted after tests so the next run uses defaults).
- Start with Windows value is not present in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Verified behavior

- Release compilation: 0 warnings, 0 errors in `build-check`.
- Inno Setup compilation succeeded for `CryoUtils-Setup-0.0.100.exe`; Windows version metadata reads `0.0.100`.
- Setup SHA-256: `cebb946034065aaa0d42599205158da896129b859071266b9a7ab5b6f2cd990a`.
- Portable zip SHA-256: `cf2300990febc81ad28f4aba28047e4b142780f0532397bb5b327c207d866195`.
- Portable zip: 470 entries; `CryoUtils.exe`, `CryoUtils.dll`, `VERSION`, `LICENSE`, and `NOTICE` present; no `.pdb` entries.
- OCR workflow, capture, hotkeys, clipboard, compressor, pinner, localization, and themes from the previous verification pass remain unchanged by the packaging work.
- Startup, dashboard, tray process lifetime. Tray icon visible as `CryoUtils`.
- Closing with Run in tray on hid the dashboard and kept the process alive; closing with it off exited the process.
- Enabling Run on startup wrote `"CryoUtils.exe" --background` under HKCU Run.
- Capture: 2 overlays opened for 2 monitors; Esc restored the dashboard.
- Drag selection (120,120) to (360,260), Ctrl+C produced a 240 x 140 clipboard image.
- Color picker opened from Ctrl+Shift+C, showed HEX/RGB/HSL and closed with Esc.
- Disabling the color module stopped its hotkey; re-enabling restored it.
- Runtime English to Arabic switch changed tab labels without a crash; the tray menu rebuilt on the same switch; theme switch to Light worked.
- GIF writer: 3 frames, 64 x 64, 1179 bytes in the encoder harness.
- MP4 writer: 2 frames, 1594 bytes, no access violation.
- OCR: recognized `HELLO CRYOUTILS 123` from a rendered test image.
- OCR result harness: small text was 640 x 250 (72 px text box); 6-line code was 640 x 408 (153 px); Markdown load was 640 x 294 (116 px); no fixed empty-height gap.
- OCR result color parser: hex, RGB, RGBA, HSL and HSLA examples parsed in the expected order and values.
- OCR result file loader: `.md` and `.cs` loaded through the same `LoadFile` path used by Open and drag/drop.
- Compressor: PNG, JPG, WebP and GIF produce previews; JPG and WebP shrink; PNG and GIF return the original when re-encoding is larger.
- Clipboard listener captured a new text clip; one item found in the in-memory list.
- Window pinner set `WS_EX_TOPMOST`, reported the focused window title, and removed topmost on unpin.
- Disabling the pinner module now unpins any tracked window before its border tracker is stopped.
- OCR result: one-line text opens at 640 x 250 with a 72 px box and no vertical dead space; code/Markdown/color layouts were rendered in the harness.
- OCR result: one line metadata reads `One line, 37 characters`; code metadata reads line and character counts.
- OCR result loads `.md` as wrapped prose, `.cs` in mono code mode with horizontal scrolling, and Arabic text with RTL flow.
- Localization dictionaries: 298 keys each, no duplicate keys, no placeholder mismatches.
- The computer has two monitors, both 1920 x 1080 at 96 DPI (100% scaling).

## Pending or unverified

- The per-user installer was compiled but not executed; only the publish payload and installer metadata were checked.
- The GitHub release workflow has not run on GitHub, and 0.0.100 has not been published.
- Authenticode signing is optional and was not exercised because no signing secrets were provided.
- The in-app updater has not completed a real GitHub release round trip.
- Mixed-DPI capture was not exercised because both test monitors run at 100%.
- PrintScreen conflict handling was not exercised because the saved hotkey was changed to Ctrl+Shift+S for testing. The banner logic is present.
- The GitHub updater was not run against a published `ZerroDevs/CryoUtils` release. It handles API errors and missing assets, but a real release/asset round trip is unverified.
- Recorded GIF/MP4 files were validated by file output, not by visual playback in a media player.
- The recording bar opened from a selected region and its Cancel button was reachable; the red frame was constructed with the bar but was not separately inspected.
- Plain-text paste was not exercised in a third-party editor.
- OCR Open/Save dialogs and real drag/drop were not clicked in this environment; the shared `LoadFile` path they call was verified with `.md` and `.cs` files.
- OCR color-swatch click/copy was not clicked in the harness; the click handler uses the same clipboard/toast path as the existing color picker.
- Markdown is accepted and editable as source text; it is not rendered as formatted Markdown by design.
- Tray icon visuals were previewed at 16/20/24 px; live placement in the notification area was not inspected.

## Next steps

1. Publish the `v0.0.100` GitHub release with `CryoUtils-Setup-0.0.100.exe` and the portable zip, then test the updater end to end against the real release.
2. Run the installer as a standard user, confirm the per-user path and shortcuts, then test uninstall.
3. Test capture and annotation alignment on one 125% and one 150% monitor.
4. Test PrintScreen when Windows Snipping Tool owns the key and confirm the fallback banner.
5. Play a recorded MP4 and GIF in Windows Media Player and a browser.

## Important paths

- Project: `C:\Users\Gaming\Desktop\Folders-Z\userscript\WebAPPS\APPS\Cryo-Family\CryoUtils`
- Reference, read only: `C:\Users\Gaming\Desktop\Folders-Z\userscript\WebAPPS\APPS\Comile`
- Debug build output: `bin\Debug\net8.0-windows10.0.19041.0\CryoUtils.exe`
- Installer: `Installer\Output\CryoUtils-Setup-0.0.100.exe`
- Portable zip: `release\CryoUtils-0.0.100-win-x64.zip`
- Temporary harnesses were removed after verification.
