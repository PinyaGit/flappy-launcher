@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion

title Flappy Launcher — Publish to CDN
echo ======================================================
echo   Flappy Launcher — Сборка и публикация на CDN
echo ======================================================
echo.
echo   Целевой сервер (DEV CDN): \\192.168.1.119\cdn\launcher
echo.
echo   Скрипт выполнит:
echo     1. Сборку Release .NET 8 (Single-File win-x64)
echo     2. Упаковку Flappy-Launcher.zip (включая 7-Zip DLL)
echo     3. Расчёт SHA256 и генерацию version.json
echo     4. Автоматическую отправку на CDN (Samba)
echo.

set "NEW_VER="
set /p "NEW_VER=  Версия лаунчера (Enter для автоопределения из .csproj): "

set "NOTES="
set /p "NOTES=  Заметки к обновлению / Notes (Enter чтобы пропустить): "

echo.
echo  ------------------------------------------------------
echo   Запуск процесса сборки и публикации...
echo  ------------------------------------------------------
echo.

if defined NEW_VER (
    if defined NOTES (
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Launcher.ps1" -Version "%NEW_VER%" -Notes "%NOTES%"
    ) else (
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Launcher.ps1" -Version "%NEW_VER%"
    )
) else (
    if defined NOTES (
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Launcher.ps1" -Notes "%NOTES%"
    ) else (
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Launcher.ps1"
    )
)

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo  [ОШИБКА] Сборка или отправка завершилась с ошибкой (%ERRORLEVEL%).
    echo.
) else (
    echo.
    echo  [УСПЕХ] Лаунчер успешно собран и загружен на CDN!
    echo.
)

pause
