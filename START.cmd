@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "APP_VERSION=1.4.4"
if exist "%~dp0VERSION.txt" set /p APP_VERSION=<"%~dp0VERSION.txt"
set "BUILD_REVISION=1.4.4-pkg-file-editor-r30"

rem One-click launcher. Elevate once because exFAT mounting requires Administrator rights.
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

cd /d "%~dp0"

rem Recompile only when this exact internal revision has already been built.
if not exist "%~dp0FpkgExfat.dll" goto prepare
if not exist "%~dp0.build-revision" goto prepare
set /p BUILT_REVISION=<"%~dp0.build-revision"
if "%BUILT_REVISION%"=="%BUILD_REVISION%" goto launch

:prepare
call "%~dp0BUILD.cmd"
if errorlevel 1 goto failed

:launch
start "" "%~dp0FPKG-GUI-exFAT.exe"
exit /b 0

:failed
echo.
echo The application could not be prepared. See build.log for details.
if exist "%~dp0build.log" type "%~dp0build.log"
pause
exit /b 1
