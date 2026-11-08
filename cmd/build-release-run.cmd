@echo off
rem Publishes the Code Graph Generator as a ready-to-run Release build for Windows (win-x64) into .\run.
rem Self-contained: the .NET runtime is included, so CodeGraphGenerator.exe starts on a PC without .NET installed.
rem Reading a code base still needs a .NET SDK on that PC (Roslyn runs MSBuild through it); see README.md.
rem Not a single file on purpose: Roslyn starts its MSBuild build host from the BuildHost-* folders next to the exe.
setlocal
cd /d "%~dp0.."
set DOTNET_CLI_TELEMETRY_OPTOUT=1
title Code Graph Generator - Release build

rem Start from an empty .\run so no file from an older build stays behind.
if exist "run" (
  echo Removing the old .\run folder...
  rmdir /s /q "run"
)
if exist "run" (
  echo.
  echo Could not empty .\run. Close CodeGraphGenerator.exe if it is running from there, then try again.
  endlocal & exit /b 1
)

echo Publishing the Code Graph Generator (Release, win-x64, self-contained) to .\run ...
dotnet publish src\CodeGraphGenerator.App\CodeGraphGenerator.App.csproj ^
  -c Release -r win-x64 --self-contained true -o run -nologo ^
  -p:SatelliteResourceLanguages=en -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 (
  echo.
  echo The publish failed; the messages above say why.
  endlocal & exit /b 1
)

if not exist "run\CodeGraphGenerator.exe" (
  echo.
  echo The publish ended, but run\CodeGraphGenerator.exe is missing.
  endlocal & exit /b 1
)

echo.
echo Done: run\CodeGraphGenerator.exe
echo Copy the whole run folder to the other PC and start CodeGraphGenerator.exe there.
endlocal
