# GajaeCode Air-Gapped Installer

Standalone Windows installer source for deploying GajaeCode into an
air-gapped environment with:

- masked API-key input;
- Windows GJC installation;
- optional WSL2, tmux, and Linux GJC installation;
- reboot continuation;
- external update, browser, and web-search disabling;
- sanitized failure logs and recovery state;
- an AGENTS-driven verification project;
- JSON evidence and a self-contained HTML report.

This repository does not contain private gateway addresses, API keys, GJC
binaries, a WSL root filesystem, or the final deployment EXE.

## Build inputs

Obtain and verify:

- `gjc-windows-x64.exe`;
- `gjc-linux-x64`;
- `gajaecode-wsl-rootfs.tar` from the included GitHub Actions workflow;
- Microsoft's `wsl_update_x64.msi`.

Build the private installer:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\installer\build.ps1 `
  -GjcBinary C:\staging\gjc-windows-x64.exe `
  -GjcLinuxBinary C:\staging\gjc-linux-x64 `
  -WslRootfs C:\staging\gajaecode-wsl-rootfs.tar `
  -WslKernelMsi C:\staging\wsl_update_x64.msi `
  -GatewayBaseUrl http://internal-gateway:8080/anthropic `
  -ModelId internal-model-id
```

Output:

```text
installer\artifacts\GajaeCode-Airgap-Setup.exe
installer\artifacts\SHA256SUMS.txt
```

The output contains private build-time deployment settings. Do not upload it
to a public release.

The rootfs workflow is stored at
`workflow-templates/build-wsl-rootfs.yml`. Move it to
`.github/workflows/build-wsl-rootfs.yml` after authenticating GitHub with the
`workflow` scope, or run `installer/build-wsl-rootfs.sh` on a Linux Docker
host.

## Target workflow

1. Run the installer as administrator.
2. Enter the server-issued bearer API key.
3. Let the installer enable WSL2 and reboot when needed.
4. Use the `GajaeCode tmux` desktop launcher.
5. Review `GajaeCode-Installation-Report.html` on the desktop.

See [Deployment Plan](docs/DEPLOYMENT_PLAN.md) and [Handoff](docs/HANDOFF.md).
