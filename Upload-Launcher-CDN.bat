@echo off
setlocal EnableExtensions DisableDelayedExpansion

:: ============================================================
::  Upload launcher self-update package → CDN
::
::  Local:  Publish\cdn-launcher\version.json + *.zip
::  Remote: root@YOUR_SERVER:/var/www/cdn.flappy.su/launcher/
::
::  Prefer Windows OpenSSH scp/ssh (built-in).
::  Optional: PuTTY pscp for non-interactive password (-pw).
::
::  CHANGE ME: HOST / USER / PASS / REMOTE_DIR before use.
::  Do not commit real passwords. Prefer SSH keys long-term.
:: ============================================================

:: CHANGE ME -------------------------------------------------
set "HOST=YOUR_SERVER_IP_OR_HOSTNAME"
set "USER=root"
:: CHANGE ME: put your own secret here locally; never push real values
set "PASS=CHANGE_ME_random_K8mP2qR9vX4n"
set "REMOTE_DIR=/var/www/cdn.flappy.su/launcher"
:: -----------------------------------------------------------

set "REMOTE=%USER%@%HOST%:%REMOTE_DIR%/"

set "LOCAL=%~dp0Publish\cdn-launcher"
if not exist "%LOCAL%\version.json" (
  if exist "%~dp0version.json" set "LOCAL=%~dp0"
)
if "%LOCAL:~-1%"=="\" set "LOCAL=%LOCAL:~0,-1%"

set "MODE="
set "PSCP="
set "SCP="
set "SSH="

if exist "%~dp0pscp.exe" set "PSCP=%~dp0pscp.exe"
if not defined PSCP if exist "%LOCAL%\pscp.exe" set "PSCP=%LOCAL%\pscp.exe"
if not defined PSCP if exist "C:\Program Files\PuTTY\pscp.exe" set "PSCP=C:\Program Files\PuTTY\pscp.exe"
if not defined PSCP if exist "C:\Program Files (x86)\PuTTY\pscp.exe" set "PSCP=C:\Program Files (x86)\PuTTY\pscp.exe"
if not defined PSCP (
  where pscp >nul 2>&1 && for /f "delims=" %%I in ('where pscp') do set "PSCP=%%I"
)

if exist "C:\Windows\System32\OpenSSH\scp.exe" set "SCP=C:\Windows\System32\OpenSSH\scp.exe"
if not defined SCP if exist "%SystemRoot%\System32\OpenSSH\scp.exe" set "SCP=%SystemRoot%\System32\OpenSSH\scp.exe"
if not defined SCP (
  where scp >nul 2>&1 && for /f "delims=" %%I in ('where scp') do set "SCP=%%I"
)

if exist "C:\Windows\System32\OpenSSH\ssh.exe" set "SSH=C:\Windows\System32\OpenSSH\ssh.exe"
if not defined SSH if exist "%SystemRoot%\System32\OpenSSH\ssh.exe" set "SSH=%SystemRoot%\System32\OpenSSH\ssh.exe"
if not defined SSH (
  where ssh >nul 2>&1 && for /f "delims=" %%I in ('where ssh') do set "SSH=%%I"
)

if defined PSCP (
  set "MODE=pscp"
) else if defined SCP (
  set "MODE=openssh"
) else (
  goto :NoTool
)

echo.
echo  ============================================
echo   Flappy Launcher CDN upload
echo  ============================================
echo  Local : %LOCAL%
echo  Remote: %REMOTE%
echo  Mode  : %MODE%
echo.

if not exist "%LOCAL%\version.json" (
  echo [ERR] version.json not found. Run Publish-Launcher.ps1 first.
  pause
  exit /b 1
)
if not exist "%LOCAL%\Flappy-Re-Dovah-Launcher.zip" (
  echo [ERR] Flappy-Re-Dovah-Launcher.zip not found. Run Publish-Launcher.ps1 first.
  pause
  exit /b 1
)

echo  Files:
dir /b "%LOCAL%\version.json" "%LOCAL%\Flappy-Re-Dovah-Launcher.zip"
echo.
if /i "%MODE%"=="openssh" (
  echo  OpenSSH: you will be prompted for the server password.
  echo.
)
echo  Type YES to upload.
set /p "CONFIRM=  > "
if /i not "%CONFIRM%"=="YES" (
  echo Cancelled.
  pause
  exit /b 0
)

echo.

if /i "%MODE%"=="pscp" goto :UploadPscp
goto :UploadOpenSsh

:UploadPscp
set "PLINK="
if exist "%~dp0plink.exe" set "PLINK=%~dp0plink.exe"
if not defined PLINK if exist "C:\Program Files\PuTTY\plink.exe" set "PLINK=C:\Program Files\PuTTY\plink.exe"
if not defined PLINK if exist "C:\Program Files (x86)\PuTTY\plink.exe" set "PLINK=C:\Program Files (x86)\PuTTY\plink.exe"
if defined PLINK if exist "%PLINK%" (
  echo  Ensuring remote directory...
  "%PLINK%" -batch -pw "%PASS%" %USER%@%HOST% "mkdir -p %REMOTE_DIR%"
)
echo  Uploading via pscp...
"%PSCP%" -batch -pw "%PASS%" "%LOCAL%\version.json" "%LOCAL%\Flappy-Re-Dovah-Launcher.zip" %REMOTE%
if errorlevel 1 goto :Fail
goto :Done

:UploadOpenSsh
if defined SSH (
  echo  Ensuring remote directory...
  "%SSH%" -o StrictHostKeyChecking=accept-new %USER%@%HOST% "mkdir -p %REMOTE_DIR%"
  if errorlevel 1 echo [WARN] ssh mkdir failed — continuing.
)
echo  Uploading via OpenSSH scp...
"%SCP%" -o StrictHostKeyChecking=accept-new "%LOCAL%\version.json" "%LOCAL%\Flappy-Re-Dovah-Launcher.zip" %REMOTE%
if errorlevel 1 goto :Fail
goto :Done

:Done
echo.
echo  DONE
echo  https://cdn.flappy.su/launcher/version.json
echo.
pause
exit /b 0

:Fail
echo.
echo [ERR] Upload failed. Check HOST/USER/PASS and network.
pause
exit /b 1

:NoTool
echo [ERR] Neither pscp nor OpenSSH scp found.
echo Install OpenSSH Client or PuTTY.
pause
exit /b 1
