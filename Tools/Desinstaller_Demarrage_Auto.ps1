$ErrorActionPreference = "Continue"
$TaskName = "Amusement SPK - Serveur TV"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principalCheck = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principalCheck.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Ce script doit etre lance en administrateur." }

try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue } catch { }
try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue } catch { }

try {
    $connections = Get-NetTCPConnection -LocalPort 8090 -State Listen -ErrorAction SilentlyContinue
    foreach ($connection in $connections) {
        $pidToCheck = $connection.OwningProcess
        $procInfo = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $pidToCheck) -ErrorAction SilentlyContinue
        if ($null -ne $procInfo -and $procInfo.CommandLine -match "SPK\.Streaming") {
            Stop-Process -Id $pidToCheck -Force -ErrorAction SilentlyContinue
        }
    }
}
catch { }

Write-Host "Demarrage automatique SPK desinstalle." -ForegroundColor Yellow
