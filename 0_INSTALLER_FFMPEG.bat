@echo off
title Installation FFmpeg - SPK

echo ===============================================
echo        INSTALLATION FFMPEG - SPK
echo ===============================================
echo.
echo FFmpeg sert a creer les flux HLS live en boucle.
echo.

where winget >nul 2>&1
if %errorlevel% neq 0 (
    echo ERREUR : winget n'est pas disponible sur ce PC.
    echo Installe FFmpeg manuellement puis assure-toi que ffmpeg fonctionne.
    echo.
    pause
    exit /b 1
)

winget install --id Gyan.FFmpeg -e --accept-package-agreements --accept-source-agreements

echo.
echo Installation terminee.
echo Ferme puis rouvre le terminal avant de lancer le serveur.
echo.
pause
