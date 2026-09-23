@echo off
setlocal
cd /d "%~dp0"

if /I "%~1"=="elevated" goto :admin

net session >nul 2>&1
if %errorlevel% equ 0 goto :admin

echo Demande des droits administrateur...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList 'elevated' -Verb RunAs"
exit /b

:admin
if not exist "%~dp0Installer\SPK_Server_Setup_Wizard.ps1" (
    echo.
    echo ERREUR : le wizard d'installation est introuvable.
    echo Verifie que le repo a ete telecharge au complet.
    echo.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer\SPK_Server_Setup_Wizard.ps1"
set EXITCODE=%errorlevel%

if not "%EXITCODE%"=="0" (
    echo.
    echo Le wizard s'est termine avec le code %EXITCODE%.
    pause
)

exit /b %EXITCODE%
