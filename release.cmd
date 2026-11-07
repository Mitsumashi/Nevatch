@echo off
rem Builds and publishes Nevatch (version from VERSION) and makes the Windows and Docker server packages
rem in release\<version>\. See release.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
if errorlevel 1 (
  echo.
  echo Release failed.
  pause
  exit /b 1
)
pause
