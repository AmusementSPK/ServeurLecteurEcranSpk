@echo off
title Etat serveur SPK
cd /d "%~dp0"

echo ===============================================
echo              ETAT SERVEUR SPK
echo ===============================================
echo.
echo Tache Windows :
schtasks /Query /TN "Amusement SPK - Serveur TV" /FO LIST 2>nul
if %errorlevel% neq 0 echo NON INSTALLEE
echo.
echo Health :
powershell.exe -NoProfile -Command "try { $r=Invoke-RestMethod -UseBasicParsing 'http://127.0.0.1:8090/health' -TimeoutSec 5; $r | ConvertTo-Json -Depth 6 } catch { Write-Host 'SERVEUR INJOIGNABLE' -ForegroundColor Red; exit 1 }"
echo.
echo Logs : %~dp0Logs
echo.
pause
