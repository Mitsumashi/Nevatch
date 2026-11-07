@echo off
rem Starts the Nevatch server and console. Keep this window open (minimizing is fine).
cd /d "%~dp0"
title Nevatch server
if not exist bin\xray.exe (
  echo Running first-time setup...
  node setup.js || goto :end
)
node server.js
:end
pause
