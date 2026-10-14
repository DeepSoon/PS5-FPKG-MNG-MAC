@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
call "%~dp0BUILD.cmd"
if errorlevel 1 goto failed
echo Running synthetic PKG tests. No user game package is used.
start "" /wait "%~dp0FPKG-GUI-exFAT.exe" --pkg-self-test
set "FPKG_SDK_TEST_RESULT=%ERRORLEVEL%"
if exist "%LOCALAPPDATA%\FpkgGuiExfat\pkg-self-test.txt" type "%LOCALAPPDATA%\FpkgGuiExfat\pkg-self-test.txt"
if not "%FPKG_SDK_TEST_RESULT%"=="0" goto failed
echo Synthetic PKG tests passed.
pause
exit /b 0
:failed
echo Tests failed. Review build.log and LOCALAPPDATA\FpkgGuiExfat\pkg-self-test.txt.
pause
exit /b 1
