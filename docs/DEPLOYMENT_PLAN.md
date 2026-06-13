# GajaeCode Air-Gapped Deployment Plan

## 1. Target environment

- Client OS: Windows 10 Pro 19045
- Existing shell: Git Bash, already installed
- Internal gateway: private build parameter
- API protocol: Anthropic Messages API
- Model: private build parameter
- Model server: 2 x NVIDIA H100
- External network: blocked
- Internal Git: available

## 2. Terminal decision

### Default mode: native Windows

Install the standalone Windows GJC binary and run it from Git Bash or Windows
Terminal. This mode supports the normal TUI, workflow skills, `ultragoal`, and
in-process task subagents.

It does not claim live `gjc team` support because GJC requires a tmux provider
that preserves tmux user options and session ownership metadata.

### Full team mode: WSL2 plus real tmux

Offer WSL2, a pinned Linux distribution, Linux GJC, and real tmux as an
optional administrator install. Use this mode only after an offline WSL
installation and a GJC team smoke test pass.

The official cmux application is not a candidate because it is macOS-only.
Native Windows tmux-compatible projects remain experimental until they pass
GJC session tagging, pane creation, environment propagation, detach/attach,
and team shutdown tests.

WSL installation may enable Windows optional features and require a reboot.
The main installer must report this as a separate phase and resume after
reboot rather than treating it as a normal user-level file copy.

## 3. Internal Git release contents

```text
gajae-code-airgap/
  installer/
    GajaeCode-Airgap-Setup.exe
    SHA256SUMS.txt
    build-info.json
  wsl/
    wsl-package.msix
    distro.tar
    tmux-offline-bundle.tar.zst
    gjc-linux-x64
    install-wsl.ps1
  verification/
    project-template/
    run-verification.ps1
    render-report.ps1
  docs/
    operations.html
    rollback.html
  licenses/
  source/
    gajae-code-source.tar.zst
```

Every executable and archive must have a SHA-256 entry. `build-info.json`
records the upstream repository, commit, version, build date, build host, .NET
SDK, Bun version, Rust toolchain, and native-addon variant. API keys are never
committed.

## 4. Installer workflow

1. Verify installer and embedded payload hashes.
2. Prompt for the server-issued bearer API key with a masked input.
3. Install `gjc.exe` under `%LOCALAPPDATA%\GajaeCode\bin`.
4. add the install directory to the user `PATH`.
5. Store the key in the user environment as `GJC_INTERNAL_API_KEY`.
6. Back up existing GJC model and runtime configuration.
7. Configure the internal provider and map all roles to the build-time model.
8. Disable update checks, marketplace updates, star reminders, web search,
   and browser tools.
9. Detect and configure the existing Git Bash installation. Fail with a
   persistent diagnostic when it is missing; do not install Git for Windows.
10. Install bundled GJC workflow definitions.
11. Test the gateway, model response, and GJC print mode.
12. Create the verification project and run the verification suite.
13. Generate HTML and JSON reports without secret values.
14. Optionally start the administrator WSL2/tmux installation phase.

Every attempt writes a timestamped sanitized log, an atomic latest-state JSON
record, and a recovery guide under
`%LOCALAPPDATA%\GajaeCode\diagnostics`. Failed logs are retained across retries
so a different agent session can resume from the recorded stage.

## 5. Verification project

Create `%USERPROFILE%\GajaeCode-Verification` as a disposable Git repository.
It contains:

- `AGENTS.md` with mandatory planning, testing, evidence, and final-response
  rules;
- a small application with one intentional defect;
- deterministic unit tests;
- independent files for parallel read-only analysis;
- expected result manifests;
- scripts that reset the project before each trial.

### Test groups

| Group | Purpose | Pass evidence |
|---|---|---|
| Installation | Binary, PATH, shell, config paths | version and path records |
| Security | No secret in YAML/report; external features disabled | config inspection |
| Gateway | Authentication and model availability | successful `/v1/messages` |
| Tool calls | read, edit, write, shell, test loop | changed files and passed tests |
| Rules | `AGENTS.md` mandatory steps followed | plan, test, evidence markers |
| Workflow | deep-interview, ralplan, ultragoal state | expected `.gjc` artifacts |
| Continuation | long multi-tool task completes without manual `continue` | terminal completion record |
| Subagents | two independent delegated checks | two completed task artifacts |
| Team dry-run | team state schema and worktree plan | valid team JSON state |
| Team live | tmux panes, worker ACK, task completion, shutdown | tmux/team receipts |
| Offline | update, marketplace, browser, web search disabled | effective-setting checks |

The rules test should run multiple trials and report a compliance rate rather
than treating one successful response as proof. Initial acceptance is 5/5 for
deterministic harness checks and at least 4/5 for model-dependent instruction
compliance.

## 6. HTML installation report

Generate:

```text
%USERPROFILE%\Desktop\GajaeCode-Installation-Report.html
%USERPROFILE%\.gjc\verification\latest.json
```

The HTML is self-contained and works without a web server. It includes:

- installation timestamp, GJC version, binary SHA-256, and build commit;
- gateway and model identifiers, with the API key shown only as present or
  missing;
- effective offline settings and an explicit enabled/disabled table;
- Git Bash, WSL2, tmux, and team-mode availability;
- each verification command, duration, exit code, and sanitized output;
- instruction-compliance score and missing evidence;
- concurrency benchmark results;
- chosen task concurrency and recommended team worker count;
- green, yellow, or red overall status;
- remediation commands and rollback paths.

## 7. H100 concurrency tuning

Do not infer serving capacity from GPU count alone. The gateway may use two
model replicas, two-GPU tensor parallelism, or a scheduler with continuous
batching.

Run a bounded benchmark at concurrency levels 1, 2, 3, and 4 using the same
short prompt and output cap. Record success rate, time to first token when
available, total latency, and HTTP/provider errors.

Initial settings before measurement:

```yaml
task:
  eager: false
  maxConcurrency: 2
  maxRecursionDepth: 1
  maxRuntimeMs: 1800000
  enableLsp: false
  forkContext:
    enabled: false
```

Selection policy:

- choose `1` if concurrency 2 produces errors, queue stalls, or more than
  twice the single-request latency;
- choose `2` as the normal target when concurrency 2 is stable;
- choose `3` or `4` only when repeated tests show zero errors and acceptable
  latency growth;
- cap normal `task.maxConcurrency` at `2` for the first production rollout,
  even when the synthetic benchmark supports more;
- calculate team workers separately because the leader also consumes model
  capacity;
- start live team mode with 1 worker for capacity 2, or 2 workers for measured
  capacity 3 or greater;
- never use GJC's built-in default of 3 team workers without a passing
  concurrency benchmark.

Re-run tuning after any model, quantization, context-window, gateway, vLLM,
tensor-parallel, or batching configuration change.

## 8. Delivery phases

### Phase A: native pilot

- Complete and sign the native installer.
- Run all non-tmux tests.
- Pilot on one clean Windows 10 PC.
- Observe at least ten real coding tasks and capture unexplained stops.

### Phase B: WSL2/tmux pilot

- Validate offline WSL installation and reboot continuation.
- Install Linux GJC and real tmux.
- Validate Windows repository and tool interoperability.
- Enable live team tests with one worker, then two.

### Phase C: production release

- Freeze hashes and build metadata.
- Publish an immutable internal Git tag/release.
- Document rollback to the previous binary and configuration backup.
- Require a new verification report after each upgrade.

## 9. Acceptance criteria

- A fresh PC requires only the installer EXE, an API key, and administrator
  approval when WSL mode is selected.
- No external download occurs during installation or verification.
- No API key appears in Git, YAML, logs, HTML, JSON, or process arguments.
- The internal gateway and structured tool calls pass.
- The verification project completes without manual `continue`.
- Offline controls are all reported as disabled/enforced.
- The selected concurrency is backed by recorded benchmark evidence.
- Live team mode is exposed only when real tmux compatibility tests pass.
