@echo off
setlocal
if not exist "%~dp0src\Syphon.Desktop\bin\Release\net10.0-windows\Syphon.exe" (
  echo Build Syphon first with build.ps1.
  pause
  exit /b 1
)
if exist "%~dp0.tools\dotnet\dotnet.exe" (
  set "DOTNET_ROOT=%~dp0.tools\dotnet"
  set "DOTNET_ROOT_X64=%~dp0.tools\dotnet"
)
start "" /d "%~dp0" "%~dp0src\Syphon.Desktop\bin\Release\net10.0-windows\Syphon.exe"
