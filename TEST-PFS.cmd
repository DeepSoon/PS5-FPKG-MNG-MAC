@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
"%~dp0tools\pfs\python\python.exe" -I -B -X utf8 "%~dp0tests\pfs_roundtrip.py"
set "FPKG_PFS_TEST_EXIT=%ERRORLEVEL%"
if not "%FPKG_PFS_TEST_EXIT%"=="0" pause
exit /b %FPKG_PFS_TEST_EXIT%
