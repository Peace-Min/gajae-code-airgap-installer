# Air-Gapped Deployment Agent Contract

This directory owns the offline Windows deployment, verification, recovery,
and optional WSL2/tmux integration for GajaeCode.

## Fixed environment

- Client: Windows 10 Pro 19045
- Git Bash: already installed and required
- Gateway: supplied as a private build parameter
- Protocol: Anthropic Messages API
- Model: supplied as a private build parameter
- Server: 2 x H100
- External network: blocked
- Internal Git: available

Do not add a Git for Windows installer to the bundle. Detect the existing Git
Bash installation and fail with a precise diagnostic when it is unavailable.
Do not commit private gateway addresses or model-server details. Store them in
`LOCAL_DEPLOYMENT.md`, which is intentionally ignored by Git.

## Secret handling

- Never commit, print, log, serialize, or place the bearer API key in a process
  argument.
- Store the key only in the Windows user environment variable
  `GJC_INTERNAL_API_KEY`.
- Configuration files reference the environment variable name, not its value.
- HTML, JSON, logs, screenshots, and test artifacts may report only whether a
  key is present.
- Redact the active key from exception text and subprocess output before
  persisting diagnostics.

## Failure continuity

Every installer and verifier run must leave enough sanitized evidence for a
different agent session to continue without repeating discovery.

Required artifacts:

- `%LOCALAPPDATA%\GajaeCode\diagnostics\install-<timestamp>.log`
- `%LOCALAPPDATA%\GajaeCode\diagnostics\latest-install-state.json`
- `%LOCALAPPDATA%\GajaeCode\diagnostics\RECOVERY.md`
- timestamped backups of overwritten GJC configuration

State JSON must identify the last stage, success/failure status, sanitized
error, log path, gateway, model, provider, and whether credentials were
present. Use atomic replacement for the latest state file.

Do not delete failed logs after a retry. A successful run creates a new log and
updates the latest-state pointer.

## Resume procedure

When continuing a failed deployment:

1. Read `docs/HANDOFF.md`.
2. Read `latest-install-state.json`.
3. Read the referenced log and the newest configuration backups.
4. Reproduce only the failed stage when possible.
5. Fix source or payload on the staging PC.
6. Build and test without using a real API key in source or command history.
7. Re-run the full installer and retain previous evidence.
8. Update `docs/HANDOFF.md` with findings, changes, tests, and remaining work.

## Verification

- Run `git diff --check`.
- Build the installer project in Release mode.
- Test failure paths as well as success paths.
- Confirm logs and state contain no key value.
- Confirm Git Bash absence is a clear hard failure.
- Confirm repeat installation is idempotent and creates configuration backups.
- Never claim live `gjc team` support until real tmux ownership tagging,
  pane creation, worker ACK, completion, and shutdown tests pass.

Do not commit unless explicitly requested.
