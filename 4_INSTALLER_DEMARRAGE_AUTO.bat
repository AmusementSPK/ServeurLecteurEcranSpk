@echo off
title Installation demarrage automatique - SPK
cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ERREUR : lance ce fichier avec CLIC DROIT ^> Executer en tant qu administrateur.
    echo.
    pause
    exit /b 1
)

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo ERREUR : .NET est introuvable.
    pause
    exit /b 1
)

where ffmpeg >nul 2>&1
if %errorlevel% neq 0 (
    echo ERREUR : FFmpeg est introuvable.
    echo Lance d abord 0_INSTALLER_FFMPEG.bat
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Installer_Demarrage_Auto.ps1"
if %errorlevel% neq 0 (
    echo.
    echo L installation a echoue.
    pause
    exit /b 1
)

echo.
echo Installation terminee.
pause
