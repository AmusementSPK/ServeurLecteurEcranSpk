@echo off
title Autoriser SPK Streaming dans le pare-feu
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ERREUR : lance ce fichier avec clic droit ^> Executer en tant qu'administrateur.
    echo.
    pause
    exit /b 1
)

netsh advfirewall firewall delete rule name="SPK Roku Streaming 8090" >nul 2>&1
netsh advfirewall firewall add rule name="SPK Roku Streaming 8090" dir=in action=allow protocol=TCP localport=8090 profile=private

echo.
echo Port TCP 8090 autorise sur le reseau prive.
echo.
pause
