@echo off
setlocal EnableExtensions DisableDelayedExpansion

:: ============================================================
::  Upload launcher self-update package to CDN (PUBLIC TEMPLATE)
::
::  This file ships with PLACEHOLDERS only.
::  Copy to Upload-Launcher-CDN.local.bat (gitignored) and fill
::  HOST / USER / PASS / HOSTKEY for your server.
:: ============================================================

set "HOST=YOUR.CDN.HOST"
set "USER=YOUR_SSH_USER"
set "PASS=CHANGE_ME"
set "REMOTE_DIR=/var/www/cdn.example/launcher"
set "REMOTE=%USER%@%HOST%:%REMOTE_DIR%/"
:: pscp -batch needs host key fingerprint, e.g. SHA256:...
set "HOSTKEY=SHA256:REPLACE_ME"

set "LOCAL=%~dp0Publish\cdn-launcher"
if not exist "%LOCAL%\version.json" (
  if exist "%~dp0version.json" set "LOCAL=%~dp0"
)
if "%LOCAL:~-1%"=="\" set "LOCAL=%LOCAL:~0,-1%"

if /i "%HOST%"=="YOUR.CDN.HOST" (
  echo [ERR] Edit this script ^(or use a *.local.bat copy^) and set real HOST/USER/PASS.
  echo Do not commit secrets to git.
  pause
  exit /b 1
)

if not exist "%LOCAL%\version.json" (
  echo [ERR] version.json not found. Run Publish-Launcher.ps1 first.
  pause
  exit /b 1
)
if not exist "%LOCAL%\Flappy-Launcher.zip" (
  echo [ERR] Flappy-Launcher.zip not found. Run Publish-Launcher.ps1 first.
  pause
  exit /b 1
)

set "PSCP="
if exist "C:\Program Files\PuTTY\pscp.exe" set "PSCP=C:\Program Files\PuTTY\pscp.exe"
if not defined PSCP if exist "C:\Program Files (x86)\PuTTY\pscp.exe" set "PSCP=C:\Program Files (x86)\PuTTY\pscp.exe"
set "SCP="
if exist "%SystemRoot%\System32\OpenSSH\scp.exe" set "SCP=%SystemRoot%\System32\OpenSSH\scp.exe"

echo  Upload:
echo    %LOCAL%\version.json
echo    %LOCAL%\Flappy-Launcher.zip
echo  -^> %REMOTE%
echo.
set /p "CONFIRM=Type YES to upload: "
if /i not "%CONFIRM%"=="YES" (
  echo Cancelled.
  pause
  exit /b 0
)

if defined PSCP (
  "%PSCP%" -batch -hostkey "%HOSTKEY%" -pw "%PASS%" "%LOCAL%\version.json" "%LOCAL%\Flappy-Launcher.zip" %REMOTE%
  if errorlevel 1 goto :Fail
) else if defined SCP (
  "%SCP%" -o StrictHostKeyChecking=accept-new "%LOCAL%\version.json" "%LOCAL%\Flappy-Launcher.zip" %REMOTE%
  if errorlevel 1 goto :Fail
) else (
  echo [ERR] Neither pscp nor scp found.
  pause
  exit /b 1
)

echo DONE.
pause
exit /b 0

:Fail
echo Upload failed.
pause
exit /b 1
