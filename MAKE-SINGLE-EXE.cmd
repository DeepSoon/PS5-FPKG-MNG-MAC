@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
where powershell.exe >nul 2>&1 || goto missing
if not exist "%WINDIR%\System32\iexpress.exe" goto missing
set "WORK=%TEMP%\fpkg-sfx-%RANDOM%%RANDOM%"
mkdir "%WORK%" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -Command "$root=(Resolve-Path '.').Path; $out=Join-Path $env:TEMP ('fpkg-sfx-'+[guid]::NewGuid().ToString('N')+'.zip'); $items=Get-ChildItem -Force | Where-Object {$_.Name -notin @('PS5-FPKG-Builder-1.4.4.exe','MAKE-SINGLE-EXE.cmd')}; Compress-Archive -Path $items.FullName -DestinationPath $out -CompressionLevel Optimal; Copy-Item $out '%WORK%\payload.zip'; Remove-Item $out" || goto failed
>"%WORK%\launch.cmd" echo @echo off
>>"%WORK%\launch.cmd" echo setlocal
>>"%WORK%\launch.cmd" echo set "APP=%%LOCALAPPDATA%%\Zer0Day\PS5-FPKG-Builder-1.4.4"
>>"%WORK%\launch.cmd" echo if not exist "%%APP%%" mkdir "%%APP%%"
>>"%WORK%\launch.cmd" echo powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%%~dp0payload.zip' -DestinationPath '%%APP%%' -Force"
>>"%WORK%\launch.cmd" echo powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%%APP%%\START.cmd' -Verb RunAs -Wait"
>"%WORK%\package.sed" echo [Version]
>>"%WORK%\package.sed" echo Class=IEXPRESS
>>"%WORK%\package.sed" echo SEDVersion=3
>>"%WORK%\package.sed" echo [Options]
>>"%WORK%\package.sed" echo PackagePurpose=InstallApp
>>"%WORK%\package.sed" echo ShowInstallProgramWindow=0
>>"%WORK%\package.sed" echo HideExtractAnimation=1
>>"%WORK%\package.sed" echo UseLongFileName=1
>>"%WORK%\package.sed" echo InsideCompressed=0
>>"%WORK%\package.sed" echo CAB_FixedSize=0
>>"%WORK%\package.sed" echo CAB_ResvCodeSigning=0
>>"%WORK%\package.sed" echo RebootMode=N
>>"%WORK%\package.sed" echo InstallPrompt=
>>"%WORK%\package.sed" echo DisplayLicense=
>>"%WORK%\package.sed" echo FinishMessage=
>>"%WORK%\package.sed" echo TargetName=%~dp0PS5-FPKG-Builder-1.4.4.exe
>>"%WORK%\package.sed" echo FriendlyName=PS5 FPKG Builder 1.4.5
>>"%WORK%\package.sed" echo AppLaunched=launch.cmd
>>"%WORK%\package.sed" echo PostInstallCmd=
>>"%WORK%\package.sed" echo AdminQuietInstCmd=
>>"%WORK%\package.sed" echo UserQuietInstCmd=
>>"%WORK%\package.sed" echo SourceFiles=SourceFiles
>>"%WORK%\package.sed" echo [SourceFiles]
>>"%WORK%\package.sed" echo SourceFiles0=%WORK%\
>>"%WORK%\package.sed" echo [SourceFiles0]
>>"%WORK%\package.sed" echo %%FILE0%%=
>>"%WORK%\package.sed" echo %%FILE1%%=
>>"%WORK%\package.sed" echo [Strings]
>>"%WORK%\package.sed" echo FILE0=payload.zip
>>"%WORK%\package.sed" echo FILE1=launch.cmd
"%WINDIR%\System32\iexpress.exe" /N "%WORK%\package.sed" || goto failed
rmdir /s /q "%WORK%"
echo Created: PS5-FPKG-Builder-1.4.4.exe
exit /b 0
:missing
echo Windows PowerShell and IExpress are required to create the single-file package.
exit /b 1
:failed
if exist "%WORK%" rmdir /s /q "%WORK%"
echo Single EXE creation failed.
exit /b 1
