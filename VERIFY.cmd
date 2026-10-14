@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
call "%~dp0BUILD.cmd"
if errorlevel 1 goto failed
start "" /wait "%~dp0FPKG-GUI-exFAT.exe" --self-test
set "FPKG_EXFAT_TEST_RESULT=%ERRORLEVEL%"
if exist "%LOCALAPPDATA%\FpkgGuiExfat\self-test.txt" type "%LOCALAPPDATA%\FpkgGuiExfat\self-test.txt"
if not "%FPKG_EXFAT_TEST_RESULT%"=="0" goto failed
echo Quick module tests passed. Run TEST-PKG.cmd for synthetic fast SDK and restore tests.
pause
exit /b 0
:failed
echo Checks failed. Review build.log and the reports in LOCALAPPDATA\FpkgGuiExfat.
pause
exit /b 1
