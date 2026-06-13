@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass ^
  -File "%~dp0scripts\assemble-installer.ps1" -Run

if errorlevel 1 (
  echo.
  echo Installation launcher failed. Review the error above.
  pause
  exit /b 1
)

endlocal

