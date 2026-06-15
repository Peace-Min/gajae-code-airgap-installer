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
- Fixed the payload extraction handle lifetime defect that caused the
  installer to move a file while its own SHA-256 input stream was still open.
- Added sharing/lock-violation retries scoped to Windows errors 32 and 33.
- Added atomic configuration, state, report, and launcher replacement.
- Added installer and assembler single-instance guards.
- Added Windows and WSL task limits of concurrency 2 and recursion depth 1.
- Added safe reboot resume when the installer already runs from its stable
  resume path.
- Added Windows 11/store WSL detection so the legacy kernel MSI is skipped
  when modern WSL is already installed.
- Added UTF-16LE decoding for native `wsl.exe` management commands so existing
  distributions are detected correctly on reinstall.
- Added a clear preflight failure when the installed Windows GJC binary is
  still running during an upgrade.
- Confirmed the bundled WSL kernel MSI has a valid Microsoft signature. The
  GJC Windows binary is currently unsigned.

## Current files

- `installer/Program.cs`
- `installer/FileOperations.cs`
- `installer/SystemPrerequisites.cs`
- `installer/WslOfflineInstaller.cs`
- `installer/VerificationRunner.cs`
- `installer/build.ps1`
- `README.md`
- `docs/DEPLOYMENT_PLAN.md`
- `AGENTS.md`

## Remaining implementation

1. Add concurrency benchmarks for levels 1 through 4.
2. Validate tmux session launch. Live team mode remains out of current scope.
3. Test clean install, reinstall, partial failure, rollback, and reboot resume
   on Windows 10 Pro 19045.
4. Produce the final signed EXE, checksum, build metadata, licenses, and
   immutable internal Git release.

## Diagnostic contract

On a target PC, begin failure analysis with:

```text
%LOCALAPPDATA%\GajaeCode\diagnostics\latest-install-state.json
%LOCALAPPDATA%\GajaeCode\diagnostics\install-*.log
%LOCALAPPDATA%\GajaeCode\diagnostics\RECOVERY.md
```

Never request that a user paste the full API key into chat, an issue, a log, or
a committed file.
