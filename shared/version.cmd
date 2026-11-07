@echo off
rem Writes a C# file with the version from ..\VERSION (one line, like 1.0.0), so both programs carry it.
rem Usage: call version.cmd <output .cs file>
setlocal
set /p VERSION=<"%~dp0..\VERSION"
if "%VERSION%"=="" (
  echo VERSION file is missing or empty.
  exit /b 1
)
> "%~1" echo [assembly: System.Reflection.AssemblyVersion("%VERSION%.0")]
>> "%~1" echo [assembly: System.Reflection.AssemblyFileVersion("%VERSION%.0")]
>> "%~1" echo [assembly: System.Reflection.AssemblyInformationalVersion("%VERSION%")]
endlocal
