# Air-Gapped Deployment Handoff

Last updated: 2026-06-13

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
