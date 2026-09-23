param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [Parameter(Mandatory = $true)]
    [string]$StatusFile,

    [string]$PreventSleep = "true"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$Root = [IO.Path]::GetFullPath($Root)
$ToolsRoot = Join-Path $Root ".spk-tools"
$DotnetDir = Join-Path $ToolsRoot "dotnet"
$DotnetExe = Join-Path $DotnetDir "dotnet.exe"
$FfmpegRoot = Join-Path $ToolsRoot "ffmpeg"
$FfmpegBin = Join-Path $FfmpegRoot "bin"
$FfmpegExe = Join-Path $FfmpegBin "ffmpeg.exe"
$Downloads = Join-Path $ToolsRoot "downloads"
$Logs = Join-Path $Root "Logs"
$Project = Join-Path $Root "SPK.Streaming.csproj"
$Watchdog = Join-Path $Root "SPK_Server_Watchdog.ps1"

$TaskName = "Amusement SPK - Serveur TV"
$FirewallName = "Amusement SPK - Serveur TV 8090"
$HealthUrl = "http://127.0.0.1:8090/health"

New-Item -ItemType Directory -Force -Path $ToolsRoot, $Downloads, $Logs | Out-Null
$InstallLog = Join-Path $Logs ("installation-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")

function Write-InstallLog([string]$Text) {
    $line = "{0}  {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Text
    Add-Content -Path $InstallLog -Value $line -Encoding UTF8
}

function Write-Status(
    [int]$Percent,
    [string]$Message,
    [string]$Detail = "",
    [bool]$Done = $false,
    [bool]$Success = $false,
    [string[]]$Addresses = @()
) {
    $payload = [ordered]@{
        percent   = $Percent
        message   = $Message
        detail    = $Detail
        done      = $Done
        success   = $Success
        addresses = $Addresses
        log       = $InstallLog
        time      = (Get-Date).ToString("o")
    }

    $temp = $StatusFile + ".tmp"
    $payload | ConvertTo-Json -Compress | Set-Content -Path $temp -Encoding UTF8
    Move-Item -Force -Path $temp -Destination $StatusFile
    Write-InstallLog ($Message + $(if ($Detail) { " - " + $Detail } else { "" }))
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "L'installation doit être exécutée en administrateur."
    }
}

function Download-File([string]$Url, [string]$Destination) {
    if (Test-Path $Destination) {
        Remove-Item -Force $Destination
    }

    try {
        $bits = Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue
        if ($null -ne $bits) {
            Start-BitsTransfer -Source $Url -Destination $Destination -ErrorAction Stop
            return
        }
    }
    catch {
        Write-InstallLog ("BITS indisponible pour " + $Url + " : " + $_.Exception.Message)
    }

    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Destination
}

function Stop-ExistingServer {
    try {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }
    catch {
    }

    try {
        $connections = Get-NetTCPConnection -LocalPort 8090 -State Listen -ErrorAction SilentlyContinue
        foreach ($connection in $connections) {
            $pidToCheck = $connection.OwningProcess
            $procInfo = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $pidToCheck) -ErrorAction SilentlyContinue
            if ($null -ne $procInfo -and $procInfo.CommandLine -match "SPK\.Streaming") {
                & taskkill.exe /PID $pidToCheck /T /F | Out-Null
            }
        }
    }
    catch {
    }

    Start-Sleep -Seconds 2
}

function Install-Dotnet {
    $hasDotnet10 = $false
    if (Test-Path $DotnetExe) {
        try {
            $sdks = & $DotnetExe --list-sdks 2>$null
            $hasDotnet10 = [bool]($sdks | Where-Object { $_ -match "^10\." } | Select-Object -First 1)
        }
        catch {
            $hasDotnet10 = $false
        }
    }

    if ($hasDotnet10) {
        Write-InstallLog ".NET 10 local déjà présent."
        return
    }

    Remove-Item -Recurse -Force $DotnetDir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $DotnetDir | Out-Null

    $installer = Join-Path $Downloads "dotnet-install.ps1"
    Download-File "https://dot.net/v1/dotnet-install.ps1" $installer

    $dotnetInstallArgs = '-NoProfile -ExecutionPolicy Bypass -File "' + $installer + '" -Channel 10.0 -InstallDir "' + $DotnetDir + '" -NoPath'
    $p = Start-Process -FilePath "powershell.exe" -ArgumentList $dotnetInstallArgs -Wait -PassThru -WindowStyle Hidden

    if ($p.ExitCode -ne 0 -or -not (Test-Path $DotnetExe)) {
        throw "L'installation locale de .NET 10 a échoué (code $($p.ExitCode))."
    }

    $sdks = & $DotnetExe --list-sdks
    if (-not ($sdks | Where-Object { $_ -match "^10\." } | Select-Object -First 1)) {
        throw ".NET a été téléchargé, mais aucun SDK 10.x n'est détecté."
    }
}

function Install-Ffmpeg {
    if (Test-Path $FfmpegExe) {
        try {
            & $FfmpegExe -version | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-InstallLog "FFmpeg local déjà présent."
                return
            }
        }
        catch {
        }
    }

    $zip = Join-Path $Downloads "ffmpeg-release-essentials.zip"
    $hashFile = Join-Path $Downloads "ffmpeg-release-essentials.zip.sha256"
    $extract = Join-Path $Downloads "ffmpeg-extract"

    Download-File "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip" $zip
    Download-File "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256" $hashFile

    $hashText = Get-Content -Raw $hashFile
    $match = [regex]::Match($hashText, "(?i)\b[a-f0-9]{64}\b")
    if (-not $match.Success) {
        throw "Impossible de lire le SHA-256 officiel de FFmpeg."
    }

    $expectedHash = $match.Value.ToUpperInvariant()
    $actualHash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($expectedHash -ne $actualHash) {
        throw "Le SHA-256 du téléchargement FFmpeg ne correspond pas à la valeur officielle."
    }

    Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $extract | Out-Null
    Expand-Archive -Path $zip -DestinationPath $extract -Force

    $sourceExe = Get-ChildItem -Path $extract -Recurse -Filter "ffmpeg.exe" -File | Select-Object -First 1
    if ($null -eq $sourceExe) {
        throw "ffmpeg.exe est introuvable dans l'archive téléchargée."
    }

    $sourceBin = Split-Path -Parent $sourceExe.FullName
    Remove-Item -Recurse -Force $FfmpegRoot -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $FfmpegBin | Out-Null
    Copy-Item -Path (Join-Path $sourceBin "*") -Destination $FfmpegBin -Recurse -Force

    if (-not (Test-Path $FfmpegExe)) {
        throw "FFmpeg n'a pas été installé correctement."
    }

    & $FfmpegExe -version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg est présent, mais ne démarre pas correctement."
    }

    Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
}

function Build-Server {
    if (-not (Test-Path $Project)) {
        throw "Projet serveur introuvable : $Project"
    }

    $env:DOTNET_ROOT = $DotnetDir

    & $DotnetExe restore $Project --nologo *>> $InstallLog
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore a échoué."
    }

    & $DotnetExe build $Project -c Release --no-restore --nologo *>> $InstallLog
    if ($LASTEXITCODE -ne 0) {
        throw "La compilation Release du serveur a échoué."
    }

    $dll = Join-Path $Root "bin\Release\net10.0\SPK.Streaming.dll"
    if (-not (Test-Path $dll)) {
        throw "Le build a réussi, mais SPK.Streaming.dll est introuvable."
    }
}

function Configure-Firewall {
    try {
        Get-NetFirewallRule -DisplayName $FirewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    }
    catch {
    }

    New-NetFirewallRule -DisplayName $FirewallName -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8090 -Profile Any -RemoteAddress LocalSubnet | Out-Null
}

function Configure-Power {
    if ($PreventSleep.ToLowerInvariant() -ne "true") {
        Write-InstallLog "Mise en veille secteur laissée inchangée à la demande de l'utilisateur."
        return
    }

    & powercfg.exe /change standby-timeout-ac 0 | Out-Null
    & powercfg.exe /change hibernate-timeout-ac 0 | Out-Null
}

function Install-ScheduledTask {
    if (-not (Test-Path $Watchdog)) {
        throw "Watchdog introuvable : $Watchdog"
    }

    $arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $Watchdog + '"'
    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments -WorkingDirectory $Root
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description "Serveur permanent Roku/VIDAA Amusement SPK avec watchdog et redémarrage automatique." -Force | Out-Null
}

function Wait-ForHealth {
    for ($i = 0; $i -lt 45; $i++) {
        Start-Sleep -Seconds 2
        try {
            $health = Invoke-RestMethod -UseBasicParsing -Uri $HealthUrl -TimeoutSec 5
            if ($health.status -eq "OK" -and $health.hlsSupervisorHealthy -eq $true) {
                return $true
            }
        }
        catch {
        }
    }

    return $false
}

function Get-LanAddresses {
    try {
        return @(
            Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
                Where-Object {
                    $_.IPAddress -ne "127.0.0.1" -and
                    $_.IPAddress -notlike "169.254.*" -and
                    $_.AddressState -eq "Preferred"
                } |
                Select-Object -ExpandProperty IPAddress -Unique
        )
    }
    catch {
        return @()
    }
}

try {
    Assert-Administrator

    Write-Status 2 "Préparation de l'installation" "Vérification du PC et arrêt de l'ancienne instance."
    Stop-ExistingServer

    Write-Status 10 "Installation de .NET 10" "Installation locale et indépendante de Windows."
    Install-Dotnet

    Write-Status 32 "Installation de FFmpeg" "Téléchargement de la version Windows et vérification SHA-256."
    Install-Ffmpeg

    Write-Status 55 "Compilation du serveur" "Restauration des dépendances .NET et build Release."
    Build-Server

    Write-Status 68 "Configuration du réseau" "Ouverture du port 8090 uniquement pour le réseau local."
    Configure-Firewall

    Write-Status 76 "Configuration de l'alimentation" "Le PC serveur ne doit pas se mettre en veille pendant son utilisation."
    Configure-Power

    Write-Status 84 "Installation du démarrage automatique" "Création du watchdog Windows sous le compte SYSTEM."
    Install-ScheduledTask

    Write-Status 90 "Démarrage du serveur" "Lancement de la tâche Windows et test de santé."
    Start-ScheduledTask -TaskName $TaskName

    if (-not (Wait-ForHealth)) {
        throw "Le serveur n'a pas répondu correctement à /health après son démarrage. Consulte le journal d'installation et Logs\watchdog.log."
    }

    $addresses = Get-LanAddresses
    $detail = if ($addresses.Count -gt 0) {
        "Serveur accessible sur http://$($addresses[0]):8090/"
    }
    else {
        "Serveur sain sur http://localhost:8090/"
    }

    Write-Status 100 "Installation terminée" $detail $true $true $addresses
}
catch {
    $message = $_.Exception.Message
    Write-InstallLog ("ECHEC : " + $message)
    Write-Status 100 "Installation échouée" $message $true $false @()
    exit 1
}
