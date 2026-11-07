@echo off
rem Lets your friend's connections through Windows Firewall.
rem Right-click this file and choose "Run as administrator".
rem Usage: allow-firewall.cmd [port]   (default 443, must match the port in the console)
set PORT=%~1
if "%PORT%"=="" set PORT=443

net session >nul 2>&1
if errorlevel 1 (
  echo This needs administrator rights: right-click allow-firewall.cmd and choose "Run as administrator".
  pause
  exit /b 1
)

netsh advfirewall firewall delete rule name="Nevatch (Xray)" >nul 2>&1
netsh advfirewall firewall add rule name="Nevatch (Xray)" dir=in action=allow protocol=TCP localport=%PORT% profile=any
if errorlevel 1 (
  echo Could not add the firewall rule.
) else (
  echo Done: incoming TCP port %PORT% is allowed.
)
pause
