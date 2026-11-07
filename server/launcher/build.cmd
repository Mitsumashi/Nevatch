@echo off
rem Builds "..\Nevatch Server.exe" (the server window) with the C# compiler that ships with Windows.
rem Usage: build.cmd [output .exe] [payload.zip]
rem   output   default ..\Nevatch Server.exe (quit Nevatch Server first when replacing it)
rem   payload  the server files, Xray and Nevatch.exe packed inside, for the single-file release (release.ps1 makes it)
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo C# compiler not found at %CSC%
  exit /b 1
)
set OUT=%~1
if "%OUT%"=="" set OUT=..\Nevatch Server.exe
set PAYLOAD=
if not "%~2"=="" set PAYLOAD="/resource:%~2,payload.zip"
if not exist obj mkdir obj
call ..\..\shared\version.cmd obj\Version.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 "/out:%OUT%" /win32icon:..\..\shared\nevatch.ico %PAYLOAD% /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll NevatchServer.cs ConsolePages.cs ..\..\shared\NevatchUi.cs obj\Version.cs
if errorlevel 1 exit /b 1
echo Built %OUT%
