@echo off
title SPK Roku HLS + Administration Web - MODE MANUEL
cd /d "%~dp0"

echo ===============================================
echo       SPK ROKU HLS + ADMINISTRATION WEB
echo                MODE MANUEL
echo ===============================================
echo.
echo Ce fichier sert aux tests et a la maintenance.
echo Pour le fonctionnement permanent, utilise :
echo   4_INSTALLER_DEMARRAGE_AUTO.bat
echo.
echo Interface : http://localhost:8090/
echo Sante     : http://localhost:8090/health
echo.
echo CTRL+C pour arreter ce lancement manuel.
echo ===============================================
echo.

where ffmpeg >nul 2>&1
if %errorlevel% neq 0 (
    echo ERREUR : FFmpeg n'est pas trouve.
    echo Lance d'abord 0_INSTALLER_FFMPEG.bat
    echo.
    pause
    exit /b 1
)

dotnet run --project "%~dp0SPK.Streaming.csproj"

pause
