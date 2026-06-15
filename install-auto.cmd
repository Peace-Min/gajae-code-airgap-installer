@echo off
setlocal
cd /d "%~dp0"

rem Unattended, self-healing installer. Assembles the setup exe, then drives it
rem non-interactively and retries through transient antivirus file locks
rem (AhnLab V3, Defender, etc.) so the install completes in any environment.
rem The interactive GUI flow (install.cmd) remains available unchanged.

powershell.exe -NoProfile -ExecutionPolicy Bypass ^
  -File "%~dp0scripts\auto-install.ps1"

endlocal
