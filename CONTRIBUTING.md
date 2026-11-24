# Contributing to CryoUtils

Thank you for contributing to **CryoUtils**.

CryoUtils is a lightweight Windows capture, OCR, and desktop utility suite created by **ZeroNuxi** under **ZerroDevs**. Bug reports, localization fixes, capture and OCR improvements, and focused pull requests are welcome.

---

## Guiding Principles

1. **Standard-user operation**: CryoUtils is `asInvoker`. Do not add an administrator requirement, a service, a driver, or a machine-wide installer action without a clear security and UX reason.
2. **Local and private by default**: Do not add telemetry, analytics, advertising, accounts, or hidden network calls. The only normal network path is the GitHub update check and download.
3. **Lightweight**: Avoid unnecessary dependencies and background workers. Capture and tool windows should close cleanly and release their resources.
4. **CryoUtils visual language**: Keep the flat dark surfaces, crisp borders, teal accent, small radii, and readable typography. Light and System themes must remain consistent. Do not hardcode colors that should be theme tokens.
5. **Complete localization**: Every user-facing string belongs in both `Resources/Strings.en.xaml` and `Resources/Strings.ar.xaml`. Arabic layouts mirror, while shortcuts, paths, versions, and technical values stay LTR.
6. **Do not break capture correctness**: Capture coordinates are physical pixels. Export from the frozen source frame plus annotations, never from the dimmed overlay.

---

## Development Environment

### Prerequisites

- Windows 10 version 2004 (build 19041) or later, or Windows 11, x64.
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), version `8.0.300` or newer recommended.
- Visual Studio 2022 with the .NET desktop development workload, VS Code with C# Dev Kit, or JetBrains Rider.
- Optional: [Inno Setup 6.3 or newer](https://jrsoftware.org/isdl.php) for installer builds. `build-installer.ps1` can install it through winget when needed.

### Get the Code

```powershell
git clone https://github.com/<your-username>/CryoUtils.git
cd CryoUtils
git checkout -b feature/your-feature-name
```

---

## Build and Run

### Restore and Build

```powershell
dotnet restore CryoUtils.csproj
dotnet build CryoUtils.csproj -c Release
```

The project targets:

```text
net8.0-windows10.0.19041.0
```

It uses WPF and Windows Forms for the tray icon and native file/color dialogs. `<ImplicitUsings>disable</ImplicitUsings>` is intentional; keep explicit usings and qualify ambiguous types such as `Color`, `Point`, `Brush`, and `MessageBox`.

### Run a Local Build

```powershell
dotnet build CryoUtils.csproj -c Debug
.\bin\Debug\net8.0-windows10.0.19041.0\CryoUtils.exe
```

Use `--background` to start in the tray without opening the dashboard:

```powershell
.\bin\Debug\net8.0-windows10.0.19041.0\CryoUtils.exe --background
```

### Build the Installer and Portable Archive

```powershell
.\build-installer.ps1
```

This publishes a self-contained win-x64 build and creates:

```text
Installer\Output\CryoUtils-Setup-<version>.exe
release\CryoUtils-<version>-win-x64.zip
```

The Inno Setup definition is `Installer\CryoUtils.iss`. It is a per-user installer under `%LOCALAPPDATA%\Programs\CryoUtils`; keep it non-elevated unless the product requirements change deliberately. The Release workflow builds the same assets on a Windows runner and publishes them with `.sha256` files.

---

## Verification

CryoUtils does not currently have a built-in automated test harness or a `/test` command. Before submitting a pull request:

- Confirm the affected configuration builds with zero errors.
- Exercise the changed flow manually.
- For capture changes, verify selection, resize, annotation export, copy, and save.
- For OCR changes, test a prose result, a code result, color codes, a Markdown file, and Arabic text where applicable.
- For settings/localization changes, test English and Arabic, Dark and Light.
- For installer or updater changes, build the installer, verify the per-user install path, test silent `/S` installation, and check the restart flow.
- Report what you could not test, especially mixed-DPI monitors, real playback of GIF/MP4, and signed release assets.

Do not claim a test passed if it was not run. The repository history and `HANDOFF.md` notes may describe older verification results; verify the current change yourself.

---

## Architecture and Code Hygiene

### 1. Application lifecycle

`App.xaml.cs` owns the tray, dashboard, capture, settings, theme/language application, hotkeys, clipboard listener, and exit cleanup. Do not create a second settings store or a second global hotkey host.

### 2. Capture geometry

- Use `ScreenCaptureService` for monitor enumeration, freeze-frame capture, crops, and physical-pixel helpers.
- Use `CaptureSurface` for shared selection and annotation state.
- Keep annotations in the scene model and render the export from the original bitmap.
- Blur is destructive in the exported image; it is not a reversible overlay.

### 3. OCR and text tools

- Use `OcrService` for Windows OCR.
- Use `TextAnalysisService` for code-shape detection and hex/RGB/HSL color parsing instead of duplicating regexes in windows.
- The OCR result window is intentionally auto-height and top-aligned. Do not return to a fixed-height stretched text box.
- Keep `.md` files as editable source text; formatted Markdown rendering is a separate feature, not an implicit expectation of opening a file.

### 4. UI resources

- Use `ThemeService.GetBrush` and `DynamicResource` tokens. Do not hardcode colors in XAML or code-created controls.
- Use `LocalizationService.Get` and `Format` for visible strings.
- Keep `Strings.en.xaml` and `Strings.ar.xaml` at key/placeholder parity.
- Technical tokens (paths, shortcuts, versions, HEX/RGB/HSL, code) stay LTR in Arabic layouts.

### 5. Persistence and updates

- `SettingsService` is the single JSON store at `%APPDATA%\CryoUtils\settings.json`.
- Use atomic writes through the existing service.
- The updater selects the release setup asset and restarts it after exit. If you change asset names or installer behavior, update `Services/UpdaterService.cs`, `Installer/CryoUtils.iss`, and `.github/workflows/release.yml` together.
- Do not add a second update mechanism, background service, or silent machine-wide change.

### 6. Resource ownership

Dispose hidden window sources, clipboard listeners, timers, GDI objects, and native handles when their owner closes. Capture overlays and tray resources should not survive app exit.

---

## Commit Conventions

Use [Conventional Commits](https://www.conventionalcommits.org/):

- `feat:` a user-facing feature
- `fix:` a bug fix
- `docs:` documentation
- `refactor:` behavior-preserving restructuring
- `test:` verification or test infrastructure
- `chore:` dependencies, build tooling, or maintenance

Examples:

```text
feat(ocr): add color-code swatches and Markdown file loading
fix(tray): keep the dashboard hidden when run in background is enabled
chore(release): add Inno Setup installer and release workflow
```

---

## Versioning

The root `VERSION` file uses exactly three numeric parts:

- Baseline: `0.0.100`.
- Next releases: `0.0.101`, `0.0.102`, etc.
- Release tags: `v0.0.100`, `v0.0.101`, etc.

`CryoUtils.csproj` stamps `AssemblyVersion`, `FileVersion`, and `InformationalVersion` from `VERSION`. `.github/workflows/release.yml` watches changes to `VERSION` on `main` and can also be run manually. Bump the patch before publishing a new release.

---

## Pull Request Checklist

- [ ] `dotnet build CryoUtils.csproj -c Release` completes without errors.
- [ ] The affected flow was exercised manually and the result is described in the PR.
- [ ] Any new UI strings exist in both English and Arabic dictionaries.
- [ ] No hardcoded UI colors, strings, or paths were introduced.
- [ ] Capture changes preserve physical-pixel geometry and original-frame export.
- [ ] Installer changes remain per-user and do not introduce an unexpected elevation prompt.
- [ ] Update changes keep release asset names, installer behavior, and the workflow in sync.
- [ ] `VERSION` is bumped sequentially if this is a release.
- [ ] Documentation (`README.md`, `SECURITY.md`, `HANDOFF.md`, or related notes) is updated when behavior changes.
- [ ] Commit history is focused and follows Conventional Commits.

---

## Licensing and Attribution

CryoUtils is licensed under the [Apache License, Version 2.0](LICENSE). By contributing, you agree that your contributions are licensed under Apache-2.0.

All redistributions and derivative works must preserve the original attribution in [NOTICE](NOTICE).
