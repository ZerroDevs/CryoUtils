# Security Policy

## Supported Versions

CryoUtils actively maintains security fixes for the current baseline and later releases:

| Version | Supported | Notes |
| :--- | :---: | :--- |
| `>= 0.0.100` / `main` | :white_check_mark: | Supported; current baseline `0.0.100`, incrementing `0.0.101`, `0.0.102`, etc. |
| `< 0.0.100` | :x: | Unsupported / unreleased builds |

Use the latest official release from the [CryoUtils Releases](https://github.com/ZerroDevs/CryoUtils/releases) page.

---

## Reporting a Vulnerability

**Do not report security vulnerabilities through public GitHub issues, discussions, or pull requests.**

Report privately through one of these channels:

1. **GitHub Private Vulnerability Reporting (Preferred)**
   - Open [CryoUtils Security Advisories](https://github.com/ZerroDevs/CryoUtils/security/advisories/new).
   - Click **Report a vulnerability** and submit the advisory form.
2. **Confidential Email**
   - Email the project maintainer **ZeroNuxi** at `usamau32o@gmail.com`.
   - Use `[CryoUtils Security]` in the subject line.

### What to Include

- A clear description of the issue and its potential impact.
- Reproduction steps or a proof of concept.
- Windows version, build, and architecture.
- Any relevant permissions, settings, or third-party application versions.

### Response and Disclosure

- **Acknowledgment:** aim to acknowledge within 48 hours.
- **Investigation:** validate and investigate within 5 business days where possible.
- **Disclosure:** coordinated disclosure after a fix or mitigation is ready. Credit is given unless you prefer to remain anonymous.

---

## Threat Model and Security Boundaries

CryoUtils is a local Windows desktop utility. It runs with the signed-in user's normal token and is installed per-user. It is not a security boundary between users or processes on the same machine.

### 1. Privileges and Installation

CryoUtils uses the `asInvoker` execution level in `app.manifest`. It does not request administrator elevation, install a service, register a driver, or change machine-wide security settings.

The Inno Setup installer is per-user:

```text
%LOCALAPPDATA%\Programs\CryoUtils
```

It does not request elevation, and uninstalling it leaves `%APPDATA%\CryoUtils\settings.json` in place. A local user who can modify the current user's application files or settings is outside the security boundary.

- **In scope:** an unintended privilege escalation, a path or command injection that runs with more authority than the app, or an installer action that silently changes machine-wide settings.
- **Out of scope:** attacks requiring an existing Administrator or SYSTEM session, or tampering with user-writable files by a process already running as that user.

### 2. Screen Capture, OCR, and Clipboard Data

Capture reads a frozen desktop image. OCR sends the selected image to the local Windows OCR engine. Recent captures are kept in memory; clipboard history is opt-in, memory-only, and cleared when disabled or when the process exits.

CryoUtils does not upload screenshots, OCR text, clipboard contents, file paths, or usage data. The app has no telemetry or analytics.

- **In scope:** an unexpected network upload of captured content, writing capture content to an undocumented persistent location, or retaining clipboard data after the module is disabled.
- **Out of scope:** a user intentionally saving or copying captured content, or a third-party application reading the Windows clipboard on the same desktop.

### 3. Local File Handling

OCR result files, compressor inputs, save dialogs, settings import/export, and capture output use paths selected by the user. The application does not concatenate user text into privileged shell commands. Settings writes use a temporary file and an atomic move.

- **In scope:** a path traversal or overwrite outside the user-selected target caused by application logic, or a malicious file triggering code execution without the user opening it.
- **Out of scope:** a user choosing to open or save a file in a sensitive location, or vulnerabilities inside a third-party image decoder that do not cross the application boundary.

### 4. Update Delivery

CryoUtils checks the latest release through the GitHub Releases API over HTTPS and selects a release asset ending in `.exe` or `.msi`. After the user confirms, it downloads the selected setup and starts it after the application exits. The current setup is a per-user Inno installer and is run silently by the update restart script.

Release assets are published with SHA-256 checksum files. The in-app updater currently relies on HTTPS GitHub delivery and does **not** enforce the checksum or Authenticode signature before running the setup. Release assets are signed only when the repository's optional signing secrets are configured. Verify downloaded files manually against the published `.sha256` files when integrity is important.

- **In scope:** an update URL or asset-selection bug that causes an unexpected executable to run, command injection in the restart script, or update files written outside the documented temp and rollback folders.
- **Out of scope:** compromise of GitHub, the release account, or the user's TLS trust store; a user knowingly running an unsigned release; or local modification of a user-writable installation. Improving checksum and signature enforcement in the updater is a hardening area, not a claimed control.

### 5. Third-Party Components

Image encoding and WebP handling use SkiaSharp. OCR, Media Foundation, WPF, and Windows Forms come from Microsoft. Vulnerabilities originating in those upstream components should be reported to their maintainers unless CryoUtils uses the component in a way that creates a distinct exploit path.

### 6. Privacy and Local Data

CryoUtils runs locally and has no accounts, telemetry, or advertising. The only outbound requests are GitHub update checks and downloads.

Local data includes:

- `%APPDATA%\CryoUtils\settings.json`
- `%LOCALAPPDATA%\CryoUtils\Rollback\`
- `%TEMP%\CryoUtils\update\`
- Files the user saves in the configured capture folder

No credentials, tokens, or encryption keys are intentionally collected. Clipboard history and recent captures stay in memory only. A user who enables clipboard history is responsible for the content copied while it is enabled.
