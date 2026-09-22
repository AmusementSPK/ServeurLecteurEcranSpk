# Superviseur permanent du serveur d'affichage SPK.
# Lance sous SYSTEM par le Planificateur de taches Windows.

$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$Logs = Join-Path $Root "Logs"
$Project = Join-Path $Root "SPK.Streaming.csproj"
$Dll = Join-Path $Root "bin\Release\net10.0\SPK.Streaming.dll"
$HealthUrl = "http://127.0.0.1:8090/health"

New-Item -ItemType Directory -Force -Path $Logs | Out-Null
$WatchdogLog = Join-Path $Logs "watchdog.log"
$BuildLog = Join-Path $Logs "build.log"

function Write-WatchdogLog([string]$Message) {
    $line = "{0}  {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Add-Content -Path $WatchdogLog -Value $line -Encoding UTF8
}

function Test-Health {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $HealthUrl -TimeoutSec 5
        return ($response.StatusCode -eq 200)
    }
    catch {
        return $false
    }
}

function Stop-StaleSpkOnPort {
    try {
        $connections = Get-NetTCPConnection -LocalPort 8090 -State Listen -ErrorAction SilentlyContinue
        foreach ($connection in $connections) {
            $pidToCheck = $connection.OwningProcess
            $procInfo = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $pidToCheck) -ErrorAction SilentlyContinue

            if ($null -ne $procInfo -and $procInfo.CommandLine -match "SPK\.Streaming") {
                Write-WatchdogLog ("Arret de l ancienne instance SPK bloquee. PID=" + $pidToCheck)
                & taskkill.exe /PID $pidToCheck /T /F | Out-Null
            }
        }
    }
    catch {
        Write-WatchdogLog ("Impossible de nettoyer le port 8090 : " + $_.Exception.Message)
    }
}

function Test-BuildRequired {
    if (-not (Test-Path $Dll)) { return $true }
    try {
        $dllTime = (Get-Item $Dll).LastWriteTimeUtc
        $latestSource = Get-ChildItem -Path $Root -Recurse -File -ErrorAction SilentlyContinue | Where-Object { ($_.Extension -eq ".cs" -or $_.Extension -eq ".csproj") -and $_.FullName -notmatch "\\bin\\" -and $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\Logs\\" } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        return ($null -ne $latestSource -and $latestSource.LastWriteTimeUtc -gt $dllTime)
    }
    catch { return $false }
}

function Ensure-ReleaseBuild([string]$DotnetPath) {
    if (-not (Test-BuildRequired)) { return (Test-Path $Dll) }
    Write-WatchdogLog "Code plus recent detecte : compilation Release."
    Add-Content -Path $BuildLog -Value ("===== BUILD {0} =====" -f (Get-Date)) -Encoding UTF8
    & $DotnetPath build $Project -c Release --nologo *>> $BuildLog
    $code = $LASTEXITCODE
    if ($code -eq 0 -and (Test-Path $Dll)) {
        Write-WatchdogLog "Compilation Release reussie."
        return $true
    }
    if (Test-Path $Dll) {
        Write-WatchdogLog "ERREUR de compilation (code $code). Conservation du dernier build fonctionnel."
        return $true
    }
    Write-WatchdogLog "ERREUR : aucun build fonctionnel. Nouvelle tentative dans 30 secondes."
    return $false
}

function Remove-OldLogs {
    try {
        Get-ChildItem -Path $Logs -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "watchdog.log" -and $_.Name -ne "build.log" -and $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item -Force -ErrorAction SilentlyContinue
    } catch { }
}

$createdNew = $false
$mutex = New-Object System.Threading.Mutex($true, "Global\AmusementSPK_SignageServer_Watchdog", [ref]$createdNew)
if (-not $createdNew) {
    Write-WatchdogLog "Un watchdog SPK est deja actif. Cette instance s arrete."
    exit 0
}

try {
    Write-WatchdogLog "Watchdog SPK demarre."
    while ($true) {
        Remove-OldLogs
        $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($null -eq $dotnetCommand) {
            Write-WatchdogLog "ERREUR : dotnet introuvable. Nouvelle tentative dans 30 secondes."
            Start-Sleep -Seconds 30
            continue
        }
        $ffmpegCommand = Get-Command ffmpeg -ErrorAction SilentlyContinue
        if ($null -eq $ffmpegCommand) {
            Write-WatchdogLog "ERREUR : FFmpeg introuvable. Nouvelle tentative dans 30 secondes."
            Start-Sleep -Seconds 30
            continue
        }
        if (-not (Ensure-ReleaseBuild $dotnetCommand.Source)) {
            Start-Sleep -Seconds 30
            continue
        }

        if (Test-Health) {
            Write-WatchdogLog "Une instance saine existe deja sur le port 8090. Surveillance sans doublon."
            while (Test-Health) { Start-Sleep -Seconds 10 }
            Write-WatchdogLog "L instance existante ne repond plus. Prise en charge par le watchdog."
            Stop-StaleSpkOnPort
            Start-Sleep -Seconds 3
            continue
        }

        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $stdout = Join-Path $Logs ("server-" + $stamp + ".out.log")
        $stderr = Join-Path $Logs ("server-" + $stamp + ".err.log")

        try {
            $process = Start-Process -FilePath $dotnetCommand.Source -ArgumentList @($Dll) -WorkingDirectory $Root -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        }
        catch {
            Write-WatchdogLog ("ERREUR au lancement du serveur : " + $_.Exception.Message)
            Start-Sleep -Seconds 10
            continue
        }

        Write-WatchdogLog ("Serveur lance. PID=" + $process.Id)
        $failures = 0
        $startupGrace = (Get-Date).AddSeconds(20)

        while (-not $process.HasExited) {
            Start-Sleep -Seconds 10
            if ((Get-Date) -lt $startupGrace) { continue }
            if (Test-Health) {
                $failures = 0
            }
            else {
                $failures++
                Write-WatchdogLog ("Health check echoue (" + $failures + "/3).")
            }
            if ($failures -ge 3) {
                Write-WatchdogLog "Serveur considere bloque : arret force puis redemarrage."
                try { & taskkill.exe /PID $process.Id /T /F | Out-Null } catch { }
                break
            }
        }

        try {
            $process.Refresh()
            if ($process.HasExited) {
                Write-WatchdogLog ("Serveur arrete. Code de sortie=" + $process.ExitCode + ". Redemarrage dans 5 secondes.")
            }
        }
        catch {
            Write-WatchdogLog "Serveur arrete. Redemarrage dans 5 secondes."
        }
        try { $process.Dispose() } catch { }
        Start-Sleep -Seconds 5
    }
}
finally {
    try { if ($createdNew) { $mutex.ReleaseMutex() } } catch { }
    try { $mutex.Dispose() } catch { }
}
