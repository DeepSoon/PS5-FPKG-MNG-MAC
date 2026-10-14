@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
if not exist "%~dp0build" mkdir "%~dp0build"
set "FPKG_EXFAT_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%FPKG_EXFAT_CSC%" set "FPKG_EXFAT_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%FPKG_EXFAT_CSC%" goto sdk
echo Compiling exFAT / FFPFSC / PFS to PKG integration...
"%FPKG_EXFAT_CSC%" /nologo /langversion:5 /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"%~dp0build\FpkgExfat.dll" /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /resource:"%~dp0assets\trophy-unlocker-ps5-r10-stable.elf",FpkgExfat.TrophyUnlocker.Ps5R10 /resource:"%~dp0assets\trophy-unlocker-ps5-uds-initializer-v12.elf",FpkgExfat.TrophyUnlocker.Ps5UdsV12 "%~dp0source\*.cs" > "%~dp0build.log" 2>&1
if errorlevel 1 goto failed
goto copy
:sdk
where dotnet >nul 2>&1
if errorlevel 1 goto missing
echo Compiling with the .NET SDK...
dotnet build "%~dp0source\FpkgExfat.csproj" --configuration Release --output "%~dp0build" > "%~dp0build.log" 2>&1
if errorlevel 1 goto failed
:copy
move /y "%~dp0build\FpkgExfat.dll" "%~dp0FpkgExfat.dll" >nul
if errorlevel 1 goto failed
set "APP_VERSION=1.4.4"
if exist "%~dp0VERSION.txt" set /p APP_VERSION=<"%~dp0VERSION.txt"
set "BUILD_REVISION=1.4.4-pkg-file-editor-r30"
> "%~dp0.build-version" echo %APP_VERSION%
> "%~dp0.build-revision" echo %BUILD_REVISION%
echo Compilation completed.
exit /b 0
:missing
echo The Windows .NET Framework compiler is unavailable.
echo Install .NET 9 SDK and retry. This script does not download software.
exit /b 1
:failed
echo Compilation failed. Details in build.log:
if exist "%~dp0build.log" type "%~dp0build.log"
exit /b 1
