@echo off
title SPK Roku Streaming Server

cd /d "%~dp0"

echo ============================================
echo        SPK ROKU STREAMING SERVER
echo ============================================
echo.
echo URL locale : http://localhost:8090
echo.
echo Pour les TV :
echo http://IP_DU_SERVEUR:8090/tv/1
echo http://IP_DU_SERVEUR:8090/tv/2
echo http://IP_DU_SERVEUR:8090/tv/3
echo http://IP_DU_SERVEUR:8090/tv/4
echo.
echo CTRL+C pour arreter le serveur.
echo ============================================
echo.

dotnet run --project "%~dp0SPK.Streaming.csproj"

pause
