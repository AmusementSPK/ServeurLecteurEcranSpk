@echo off
title Desinstallation demarrage automatique - SPK
cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ERREUR : lance ce fichier avec CLIC DROIT ^> Executer en tant qu administrateur.
    echo.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Desinstaller_Demarrage_Auto.ps1"
echo.
pause
