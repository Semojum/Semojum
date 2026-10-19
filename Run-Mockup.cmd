@echo off
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install .NET 10 SDK, or use the portable Windows ZIP.
  pause
  exit /b 1
)
dotnet run --project src\Semojum.Desktop -c Release
if errorlevel 1 pause
