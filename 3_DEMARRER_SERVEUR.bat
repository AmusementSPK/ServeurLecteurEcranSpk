@echo off
title SPK Roku HLS + Administration Web - MODE MANUEL
cd /d "%~dp0"

echo ===============================================
echo       SPK ROKU HLS + ADMINISTRATION WEB
echo                MODE MANUEL
echo ===============================================
echo.
echo Ce fichier sert aux tests et a la maintenance.
echo Pour installer/reparer le serveur permanent :
echo   INSTALLER_SPK.bat
echo.
echo Interface : http://localhost:8090/
echo Sante     : http://localhost:8090/health
echo.
echo CTRL+C pour arreter ce lancement manuel.
echo ===============================================
echo.

set "DOTNET_EXE=%~dp0.spk-tools\dotnet\dotnet.exe"
set "FFMPEG_EXE=%~dp0.spk-tools\ffmpeg\bin\ffmpeg.exe"

if not exist "%DOTNET_EXE%" (
    for /f "delims=" %%D in ('where dotnet 2^>nul') do (
        set "DOTNET_EXE=%%D"
        goto :dotnet_ok
    )
    echo ERREUR : .NET est introuvable.
    echo Lance INSTALLER_SPK.bat
    pause
    exit /b 1
)
:dotnet_ok

if not exist "%FFMPEG_EXE%" (
    for /f "delims=" %%F in ('where ffmpeg 2^>nul') do (
        set "FFMPEG_EXE=%%F"
        goto :ffmpeg_ok
    )
    echo ERREUR : FFmpeg est introuvable.
    echo Lance INSTALLER_SPK.bat
    pause
    exit /b 1
)
:ffmpeg_ok

set "Streaming__FfmpegPath=%FFMPEG_EXE%"
for %%D in ("%DOTNET_EXE%") do set "DOTNET_ROOT=%%~dpD"

"%DOTNET_EXE%" run --project "%~dp0SPK.Streaming.csproj"

pause
