@echo off
rem Builds dist\Nevatch.exe: Nevatch.cs and Monitor.cs plus the shared look, with xray\xray.exe and wintun\wintun.dll
rem packed inside, using the C# compiler that ships with Windows. Both are downloaded by "node setup.js"
rem in the server folder. The server adds each person's key when you download it from the console.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo C# compiler not found at %CSC%
  exit /b 1
)
if not exist xray\xray.exe (
  echo xray\xray.exe is missing. Run "node setup.js" in the server folder first.
  exit /b 1
)
if not exist wintun\wintun.dll (
  echo wintun\wintun.dll is missing. Run "node setup.js" in the server folder first.
  exit /b 1
)
if not exist obj mkdir obj
if not exist dist mkdir dist
rem The version comes from ..\VERSION.
call ..\shared\version.cmd obj\Version.cs
if errorlevel 1 exit /b 1

rem Compress them so Nevatch.exe stays small (about 14 MB instead of 35 MB).
powershell -NoProfile -Command "$ErrorActionPreference = 'Stop'; foreach ($f in @('xray\xray.exe', 'wintun\wintun.dll')) { $in = [IO.File]::OpenRead($f); $out = [IO.File]::Create('obj\' + [IO.Path]::GetFileName($f) + '.gz'); $gz = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal); $in.CopyTo($gz); $gz.Close(); $in.Close() }"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:dist\Nevatch.exe /win32icon:..\shared\nevatch.ico /resource:obj\xray.exe.gz,xray.exe.gz /resource:obj\wintun.dll.gz,wintun.dll.gz /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Management.dll Nevatch.cs Monitor.cs Qr.cs ..\shared\NevatchUi.cs obj\Version.cs
if errorlevel 1 exit /b 1
echo Built dist\Nevatch.exe
