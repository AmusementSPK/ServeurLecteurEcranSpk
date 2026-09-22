$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Watchdog = Join-Path $Root "SPK_Server_Watchdog.ps1"
$Project = Join-Path $Root "SPK.Streaming.csproj"
$TaskName = "Amusement SPK - Serveur TV"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principalCheck = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principalCheck.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Ce script doit etre lance en administrateur." }

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$null = Get-Command ffmpeg -ErrorAction Stop

Write-Host ""
Write-Host "Compilation du serveur SPK..." -ForegroundColor Cyan
& $dotnet build $Project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "La compilation Release a echoue. Le demarrage automatique n a pas ete installe." }

$arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $Watchdog + '"'
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments -WorkingDirectory $Root
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$taskPrincipal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $taskPrincipal -Description "Serveur permanent Roku/VIDAA Amusement SPK avec watchdog et redemarrage automatique." -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host ""
Write-Host "===============================================" -ForegroundColor Green
Write-Host " DEMARRAGE AUTOMATIQUE SPK INSTALLE" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Le serveur demarre maintenant sous SYSTEM."
Write-Host "Il redemarrera automatiquement apres un crash ou un redemarrage Windows."
Write-Host "Interface : http://localhost:8090/"
Write-Host "Sante     : http://localhost:8090/health"
Write-Host ("Logs      : " + (Join-Path $Root "Logs"))
Write-Host ""
