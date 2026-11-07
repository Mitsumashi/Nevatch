@echo off
rem Rebuilds Nevatch.exe from the code in ..\client. Everyone's Nevatch then updates itself through the VPN
rem the next time it connects, or within 6 hours while it stays connected. The server doesn't need a restart.
call "%~dp0..\client\build.cmd"
if errorlevel 1 (
  echo.
  echo Building failed, so nobody gets updated.
  pause
  exit /b 1
)
echo.
echo Done. Everyone's Nevatch updates itself the next time it connects,
echo or within 6 hours while it stays connected.
pause
