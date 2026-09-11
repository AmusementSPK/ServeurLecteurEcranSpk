@echo off
title SPK Roku HLS + Administration Web
cd /d "%~dp0"

echo ===============================================
echo       SPK ROKU HLS + ADMINISTRATION WEB
echo ===============================================
echo.
echo Interface : http://localhost:8090/
echo Sante     : http://localhost:8090/health
echo.
echo Les Roku continuent d'utiliser :
echo http://IP_DU_SERVEUR:8090/hls/tv1/index.m3u8
echo.
echo CTRL+C pour arreter.
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
