# Air-Gapped Deployment Handoff

Last updated: 2026-06-15

## Objective

Deliver a single Windows installer that prompts for the internal bearer API
key, installs and configures GajaeCode for the internal gateway, verifies the
connection, produces offline verification evidence, and optionally installs a
validated WSL2/tmux environment for live team mode.

## Completed

- Confirmed the private gateway, Anthropic Messages API, and internal model.
- Confirmed Git Bash is already installed and must not be bundled.
- Added the Windows Forms installer project.
- Moved private gateway and model values to build-time parameters.
- Added masked API-key input and user-environment storage.
- Added provider and role configuration.
- Disabled startup updates, marketplace updates, star reminders, web search,
  and browser tools.
- Added GJC binary SHA-256 verification.
- Added gateway and real GJC print-mode checks.
- Added persistent sanitized installation logs, atomic latest-state JSON, and
  a recovery guide.
- Added WSL2 feature enablement, reboot continuation, offline distro import,
  Linux GJC configuration, tmux verification, and desktop launchers.
- Added a GitHub Actions workflow that builds the tmux-ready WSL rootfs.
- Added a disposable AGENTS-driven verification project and HTML/JSON report.
- Downloaded and verified official v0.5.0 Windows and Linux GJC binaries into
  the ignored local staging directory.
- Added the overall deployment plan.
- Installer project builds successfully in Release mode.
- Made every installer file write antivirus-safe (branch `fix/av-safe-install`).
  Field reports showed `ExtractAndVerifyBinary` failing at
  `File.Move(gjc.exe.new -> gjc.exe)` with "The process cannot access the file
  because it is being used by another process" because AhnLab V3 real-time scan
  locks the freshly written executable. Added `RunWithFileRetry` (exponential
  backoff on sharing/lock violations, max ~15s) around the binary extract/move,
  config writes, config backups, atomic state replacement, and the recovery
  file; diagnostics logging is now best-effort and never aborts the install.
- Hardened `scripts/assemble-installer.ps1`: a process mutex makes assembly
  single-instance (two cmd windows previously collided with "Move-Item: file
  already exists"), and the part-join and final replace retry on transient
  antivirus locks.
- Added an unattended installer (`install-auto.cmd` + `scripts/auto-install.ps1`)
  for environments where an antivirus exclusion is unavailable or ineffective
  (e.g. policy-managed AhnLab V3 that ignores a local folder exclusion). It
  self-elevates, reads the API key once into `GJC_INTERNAL_API_KEY` (never an
  argument), assembles+verifies the exe, then drives the existing installer
  non-interactively via `--resume-wsl`, watching `latest-install-state.json`
  and retrying the whole run on a transient lock. Works with the current exe
  (no rebuild). The interactive `install.cmd` flow is unchanged.
- Korean-containing PowerShell scripts are now saved as UTF-8 with BOM so
  Windows PowerShell 5.1 (which reads BOM-less files as the ANSI codepage)
  parses and prints them correctly on the Korean target.

## Current files

- `installer/Program.cs`
- `installer/build.ps1`
- `README.md`
- `docs/DEPLOYMENT_PLAN.md`
- `AGENTS.md`

## Remaining implementation

1. Add concurrency benchmarks for levels 1 through 4.
2. Persist the selected `task.maxConcurrency`; start with 2 and cap the first
   production rollout at 2.
3. Validate tmux session launch. Live team mode remains out of current scope.
4. Test clean install, reinstall, partial failure, rollback, and reboot resume
   on Windows 10 Pro 19045.
5. Produce the final signed EXE, checksum, build metadata, licenses, and
   immutable internal Git release. Code-signing `gjc.exe` is strongly
   recommended: a signed binary is far less likely to be quarantined or locked
   by endpoint security than an unsigned one.
6. Rebuild required for the antivirus retry fix to reach clients: re-run
   `installer/build.ps1` (re-embeds `gjc.exe`), then `scripts/split-installer.ps1`
   to regenerate `release/parts/*` and `release/SHA256SUMS.txt`. The
   `assemble-installer.ps1` change is interpreted and takes effect immediately.
7. Apply the same `RunWithFileRetry` pattern to `WslOfflineInstaller.cs` so the
   WSL rootfs/MSI extraction is equally resilient to real-time scanning.

## Diagnostic contract

On a target PC, begin failure analysis with:

```text
%LOCALAPPDATA%\GajaeCode\diagnostics\latest-install-state.json
%LOCALAPPDATA%\GajaeCode\diagnostics\install-*.log
%LOCALAPPDATA%\GajaeCode\diagnostics\RECOVERY.md
```

Never request that a user paste the full API key into chat, an issue, a log, or
a committed file.
