@echo off
rem Builds the Code Graph Generator (Debug) and opens its window. It needs the .NET 10 SDK: Roslyn uses it to read
rem the code bases you pick. No ports, no services: the window is the whole app.
setlocal
cd /d "%~dp0.."
set DOTNET_CLI_TELEMETRY_OPTOUT=1
title Code Graph Generator

echo Building the Code Graph Generator (Debug)...
dotnet build src\CodeGraphGenerator.App\CodeGraphGenerator.App.csproj -c Debug -nologo -v quiet
if errorlevel 1 (
  echo.
  echo The build failed; the messages above say why. Nothing was started.
  endlocal & exit /b 1
)

start "" "src\CodeGraphGenerator.App\bin\Debug\net10.0-windows10.0.19041.0\CodeGraphGenerator.exe"
endlocal
