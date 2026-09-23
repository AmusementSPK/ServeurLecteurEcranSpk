Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$Root = Split-Path -Parent $PSScriptRoot
$Worker = Join-Path $PSScriptRoot "Install-SPKServer.ps1"
$StatusFile = Join-Path $env:TEMP ("spk-server-installer-" + [Guid]::NewGuid().ToString("N") + ".json")
$WorkerProcess = $null
$LastMessage = ""

function New-Label([string]$Text, [int]$X, [int]$Y, [int]$Width, [int]$Height, [float]$Size = 10, [bool]$Bold = $false) {
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.Location = New-Object System.Drawing.Point($X, $Y)
    $label.Size = New-Object System.Drawing.Size($Width, $Height)
    $style = if ($Bold) { [System.Drawing.FontStyle]::Bold } else { [System.Drawing.FontStyle]::Regular }
    $label.Font = New-Object System.Drawing.Font("Segoe UI", $Size, $style)
    return $label
}

function Show-Panel($Panel) {
    foreach ($p in @($WelcomePanel, $OptionsPanel, $ProgressPanel, $FinishPanel)) {
        if ($null -ne $p) { $p.Visible = $false }
    }
    $Panel.Visible = $true
    $Panel.BringToFront()
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Installation serveur TV SPK"
$form.Size = New-Object System.Drawing.Size(820, 610)
$form.MinimumSize = New-Object System.Drawing.Size(820, 610)
$form.MaximumSize = New-Object System.Drawing.Size(820, 610)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(246, 247, 249)

$header = New-Object System.Windows.Forms.Panel
$header.Dock = [System.Windows.Forms.DockStyle]::Top
$header.Height = 92
$header.BackColor = [System.Drawing.Color]::FromArgb(25, 28, 35)
$form.Controls.Add($header)

$brand = New-Label "AMUSEMENT SPK" 30 17 720 24 10 $true
$brand.ForeColor = [System.Drawing.Color]::FromArgb(255, 112, 70)
$header.Controls.Add($brand)

$headerTitle = New-Label "Installation du serveur d'affichage" 28 42 740 36 19 $true
$headerTitle.ForeColor = [System.Drawing.Color]::White
$header.Controls.Add($headerTitle)

$content = New-Object System.Windows.Forms.Panel
$content.Location = New-Object System.Drawing.Point(0, 92)
$content.Size = New-Object System.Drawing.Size(804, 420)
$content.BackColor = $form.BackColor
$form.Controls.Add($content)

$footer = New-Object System.Windows.Forms.Panel
$footer.Location = New-Object System.Drawing.Point(0, 512)
$footer.Size = New-Object System.Drawing.Size(804, 60)
$footer.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($footer)

$backButton = New-Object System.Windows.Forms.Button
$backButton.Text = "Précédent"
$backButton.Location = New-Object System.Drawing.Point(500, 14)
$backButton.Size = New-Object System.Drawing.Size(120, 34)
$backButton.Enabled = $false
$footer.Controls.Add($backButton)

$nextButton = New-Object System.Windows.Forms.Button
$nextButton.Text = "Suivant"
$nextButton.Location = New-Object System.Drawing.Point(632, 14)
$nextButton.Size = New-Object System.Drawing.Size(140, 34)
$nextButton.BackColor = [System.Drawing.Color]::FromArgb(255, 91, 46)
$nextButton.ForeColor = [System.Drawing.Color]::White
$nextButton.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$nextButton.FlatAppearance.BorderSize = 0
$footer.Controls.Add($nextButton)

$cancelButton = New-Object System.Windows.Forms.Button
$cancelButton.Text = "Annuler"
$cancelButton.Location = New-Object System.Drawing.Point(28, 14)
$cancelButton.Size = New-Object System.Drawing.Size(110, 34)
$footer.Controls.Add($cancelButton)

# PAGE 1
$WelcomePanel = New-Object System.Windows.Forms.Panel
$WelcomePanel.Dock = [System.Windows.Forms.DockStyle]::Fill
$content.Controls.Add($WelcomePanel)

$WelcomePanel.Controls.Add((New-Label "Bienvenue" 34 28 700 38 21 $true))
$WelcomePanel.Controls.Add((New-Label "Ce wizard transforme ce PC en serveur Roku / VIDAA SPK prêt à fonctionner." 36 76 710 32 11 $false))

$welcomeText = @"
L'installation est entièrement automatisée :

• .NET 10 est téléchargé et installé localement au serveur.
• FFmpeg est téléchargé et vérifié par SHA-256.
• Le serveur est compilé en Release.
• Le port 8090 est ouvert uniquement au réseau local.
• Le watchdog est installé sous le compte SYSTEM.
• Le serveur redémarre automatiquement après un crash ou un redémarrage Windows.
• Un test final /health confirme que le serveur fonctionne.

Aucune installation manuelle de .NET, FFmpeg ou WinGet n'est nécessaire.
"@

$welcomeBox = New-Object System.Windows.Forms.TextBox
$welcomeBox.Text = $welcomeText
$welcomeBox.Location = New-Object System.Drawing.Point(38, 122)
$welcomeBox.Size = New-Object System.Drawing.Size(720, 205)
$welcomeBox.Multiline = $true
$welcomeBox.ReadOnly = $true
$welcomeBox.BorderStyle = [System.Windows.Forms.BorderStyle]::None
$welcomeBox.BackColor = $form.BackColor
$welcomeBox.Font = New-Object System.Drawing.Font("Segoe UI", 10.5)
$WelcomePanel.Controls.Add($welcomeBox)

$pathLabel = New-Label ("Dossier serveur : " + $Root) 38 346 720 44 9 $false
$pathLabel.ForeColor = [System.Drawing.Color]::DimGray
$WelcomePanel.Controls.Add($pathLabel)

# PAGE 2
$OptionsPanel = New-Object System.Windows.Forms.Panel
$OptionsPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
$OptionsPanel.Visible = $false
$content.Controls.Add($OptionsPanel)

$OptionsPanel.Controls.Add((New-Label "Configuration" 34 28 700 38 21 $true))
$OptionsPanel.Controls.Add((New-Label "Les éléments essentiels ci-dessous seront installés automatiquement." 36 76 720 30 11 $false))

$required = New-Object System.Windows.Forms.TextBox
$required.Text = @"
✓ .NET 10 local au serveur
✓ FFmpeg local au serveur
✓ Build Release
✓ Pare-feu réseau local, port 8090
✓ Watchdog et démarrage automatique Windows
✓ Vérification finale de santé
"@
$required.Location = New-Object System.Drawing.Point(42, 120)
$required.Size = New-Object System.Drawing.Size(700, 145)
$required.Multiline = $true
$required.ReadOnly = $true
$required.BorderStyle = [System.Windows.Forms.BorderStyle]::None
$required.BackColor = $form.BackColor
$required.Font = New-Object System.Drawing.Font("Segoe UI", 11)
$OptionsPanel.Controls.Add($required)

$sleepCheck = New-Object System.Windows.Forms.CheckBox
$sleepCheck.Text = "Empêcher la mise en veille du PC lorsqu'il est branché"
$sleepCheck.Location = New-Object System.Drawing.Point(42, 282)
$sleepCheck.Size = New-Object System.Drawing.Size(650, 30)
$sleepCheck.Font = New-Object System.Drawing.Font("Segoe UI", 10.5, [System.Drawing.FontStyle]::Bold)
$sleepCheck.Checked = $true
$OptionsPanel.Controls.Add($sleepCheck)

$OptionsPanel.Controls.Add((New-Label "Recommandé pour un serveur : pendant la veille Windows, aucun service ni watchdog ne peut diffuser les menus." 64 316 675 48 9 $false))

$warning = New-Label "Important : après l'installation, ne déplace pas le dossier du serveur. Relance ce wizard à tout moment pour réparer ou réinstaller la configuration." 42 372 710 42 9 $true
$warning.ForeColor = [System.Drawing.Color]::FromArgb(145, 82, 0)
$OptionsPanel.Controls.Add($warning)

# PAGE 3
$ProgressPanel = New-Object System.Windows.Forms.Panel
$ProgressPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
$ProgressPanel.Visible = $false
$content.Controls.Add($ProgressPanel)

$ProgressPanel.Controls.Add((New-Label "Installation en cours" 34 28 700 38 21 $true))
$progressStatus = New-Label "Préparation..." 38 88 720 32 12 $true
$ProgressPanel.Controls.Add($progressStatus)

$progressBar = New-Object System.Windows.Forms.ProgressBar
$progressBar.Location = New-Object System.Drawing.Point(40, 132)
$progressBar.Size = New-Object System.Drawing.Size(715, 25)
$progressBar.Minimum = 0
$progressBar.Maximum = 100
$progressBar.Value = 0
$ProgressPanel.Controls.Add($progressBar)

$detailBox = New-Object System.Windows.Forms.TextBox
$detailBox.Location = New-Object System.Drawing.Point(40, 178)
$detailBox.Size = New-Object System.Drawing.Size(715, 185)
$detailBox.Multiline = $true
$detailBox.ReadOnly = $true
$detailBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$detailBox.Font = New-Object System.Drawing.Font("Consolas", 9)
$detailBox.BackColor = [System.Drawing.Color]::White
$ProgressPanel.Controls.Add($detailBox)

$ProgressPanel.Controls.Add((New-Label "Le téléchargement de FFmpeg est la partie la plus longue. Ne ferme pas cette fenêtre." 40 378 710 28 9 $false))

# PAGE 4
$FinishPanel = New-Object System.Windows.Forms.Panel
$FinishPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
$FinishPanel.Visible = $false
$content.Controls.Add($FinishPanel)

$finishTitle = New-Label "Installation terminée" 34 28 700 38 21 $true
$FinishPanel.Controls.Add($finishTitle)

$finishMessage = New-Label "" 38 84 710 70 11 $false
$FinishPanel.Controls.Add($finishMessage)

$addressBox = New-Object System.Windows.Forms.TextBox
$addressBox.Location = New-Object System.Drawing.Point(40, 165)
$addressBox.Size = New-Object System.Drawing.Size(715, 95)
$addressBox.Multiline = $true
$addressBox.ReadOnly = $true
$addressBox.Font = New-Object System.Drawing.Font("Consolas", 10)
$addressBox.BackColor = [System.Drawing.Color]::White
$FinishPanel.Controls.Add($addressBox)

$openServerButton = New-Object System.Windows.Forms.Button
$openServerButton.Text = "Ouvrir le panneau SPK"
$openServerButton.Location = New-Object System.Drawing.Point(40, 285)
$openServerButton.Size = New-Object System.Drawing.Size(200, 38)
$openServerButton.Visible = $false
$FinishPanel.Controls.Add($openServerButton)

$openLogsButton = New-Object System.Windows.Forms.Button
$openLogsButton.Text = "Ouvrir les logs"
$openLogsButton.Location = New-Object System.Drawing.Point(255, 285)
$openLogsButton.Size = New-Object System.Drawing.Size(150, 38)
$openLogsButton.Visible = $false
$FinishPanel.Controls.Add($openLogsButton)

$finishNote = New-Label "" 40 345 710 58 9 $false
$finishNote.ForeColor = [System.Drawing.Color]::DimGray
$FinishPanel.Controls.Add($finishNote)

$CurrentPage = 1
$InstallSuccess = $false
$FirstAddress = ""

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 600
$timer.Add_Tick({
    if (-not (Test-Path $StatusFile)) {
        if ($null -ne $WorkerProcess -and $WorkerProcess.HasExited) {
            $timer.Stop()
            $finishTitle.Text = "Installation interrompue"
            $finishTitle.ForeColor = [System.Drawing.Color]::DarkRed
            $finishMessage.Text = "Le processus d'installation s'est arrêté sans produire de résultat. Consulte les logs."
            $addressBox.Text = ""
            $openLogsButton.Visible = $true
            $finishNote.Text = "Dossier des logs : " + (Join-Path $Root "Logs")
            Show-Panel $FinishPanel
            $nextButton.Text = "Fermer"
            $nextButton.Enabled = $true
            $cancelButton.Visible = $false
        }
        return
    }

    try {
        $state = Get-Content -Raw $StatusFile | ConvertFrom-Json
    }
    catch {
        return
    }

    $p = [Math]::Max(0, [Math]::Min(100, [int]$state.percent))
    $progressBar.Value = $p
    $progressStatus.Text = [string]$state.message

    $line = ("[{0}%] {1}" -f $p, [string]$state.message)
    if ($state.detail) { $line += " - " + [string]$state.detail }

    if ($line -ne $LastMessage) {
        $script:LastMessage = $line
        $detailBox.AppendText($line + [Environment]::NewLine)
    }

    if ($state.done -eq $true) {
        $timer.Stop()
        $script:InstallSuccess = [bool]$state.success

        if ($InstallSuccess) {
            $finishTitle.Text = "Serveur SPK prêt"
            $finishTitle.ForeColor = [System.Drawing.Color]::FromArgb(0, 120, 70)
            $finishMessage.Text = "Le serveur est installé, démarré sous SYSTEM et surveillé automatiquement. Tu peux fermer ce wizard."
            $addresses = @($state.addresses)
            if ($addresses.Count -gt 0) {
                $script:FirstAddress = [string]$addresses[0]
                $lines = @("Panneau local : http://localhost:8090/")
                foreach ($ip in $addresses) {
                    $lines += ("Réseau : http://" + $ip + ":8090/")
                }
                $addressBox.Text = ($lines -join [Environment]::NewLine)
            }
            else {
                $addressBox.Text = "Panneau local : http://localhost:8090/"
            }

            $openServerButton.Visible = $true
            $openLogsButton.Visible = $true
            $finishNote.Text = "Pour une fiabilité maximale après une panne de courant, active aussi « Restore on AC Power Loss / Power On » dans le BIOS du PC."
        }
        else {
            $finishTitle.Text = "Installation échouée"
            $finishTitle.ForeColor = [System.Drawing.Color]::DarkRed
            $finishMessage.Text = "Le wizard a rencontré un problème. Aucun détail n'est caché : ouvre les logs pour voir l'étape exacte qui a échoué."
            $addressBox.Text = [string]$state.detail
            $openServerButton.Visible = $false
            $openLogsButton.Visible = $true
            $finishNote.Text = "Journal : " + [string]$state.log
        }

        Show-Panel $FinishPanel
        $CurrentPage = 4
        $backButton.Enabled = $false
        $cancelButton.Visible = $false
        $nextButton.Text = "Fermer"
        $nextButton.Enabled = $true
    }
})

$nextButton.Add_Click({
    if ($CurrentPage -eq 1) {
        $CurrentPage = 2
        Show-Panel $OptionsPanel
        $backButton.Enabled = $true
        $nextButton.Text = "Installer"
        return
    }

    if ($CurrentPage -eq 2) {
        if (-not (Test-Path $Worker)) {
            [System.Windows.Forms.MessageBox]::Show("Le fichier Installer\Install-SPKServer.ps1 est introuvable.", "SPK", "OK", "Error") | Out-Null
            return
        }

        Remove-Item -Force $StatusFile -ErrorAction SilentlyContinue
        Remove-Item -Force ($StatusFile + ".tmp") -ErrorAction SilentlyContinue

        $CurrentPage = 3
        Show-Panel $ProgressPanel
        $backButton.Enabled = $false
        $nextButton.Enabled = $false
        $cancelButton.Enabled = $false

        $sleepValue = if ($sleepCheck.Checked) { "true" } else { "false" }

        try {
            $WorkerProcess = Start-Process -FilePath "powershell.exe" -ArgumentList @(
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", $Worker,
                "-Root", $Root,
                "-StatusFile", $StatusFile,
                "-PreventSleep", $sleepValue
            ) -PassThru -WindowStyle Hidden
            $timer.Start()
        }
        catch {
            $finishTitle.Text = "Impossible de lancer l'installation"
            $finishTitle.ForeColor = [System.Drawing.Color]::DarkRed
            $finishMessage.Text = $_.Exception.Message
            $addressBox.Text = ""
            $openLogsButton.Visible = $true
            Show-Panel $FinishPanel
            $CurrentPage = 4
            $nextButton.Text = "Fermer"
            $nextButton.Enabled = $true
            $cancelButton.Visible = $false
        }
        return
    }

    if ($CurrentPage -eq 4) {
        $form.Close()
    }
})

$backButton.Add_Click({
    if ($CurrentPage -eq 2) {
        $CurrentPage = 1
        Show-Panel $WelcomePanel
        $backButton.Enabled = $false
        $nextButton.Text = "Suivant"
    }
})

$cancelButton.Add_Click({
    if ($CurrentPage -lt 3) {
        $form.Close()
    }
})

$openServerButton.Add_Click({
    $url = if ($FirstAddress) { "http://" + $FirstAddress + ":8090/" } else { "http://localhost:8090/" }
    Start-Process $url
})

$openLogsButton.Add_Click({
    $logs = Join-Path $Root "Logs"
    New-Item -ItemType Directory -Force -Path $logs | Out-Null
    Start-Process explorer.exe -ArgumentList $logs
})

$form.Add_FormClosing({
    param($sender, $e)
    if ($CurrentPage -eq 3 -and $null -ne $WorkerProcess -and -not $WorkerProcess.HasExited) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "L'installation est encore en cours. Fermer le wizard n'arrêtera pas proprement l'installation. Veux-tu vraiment fermer ?",
            "Installation SPK",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        )
        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
            $e.Cancel = $true
        }
    }
})

Show-Panel $WelcomePanel
[void]$form.ShowDialog()

$timer.Stop()
try { Remove-Item -Force $StatusFile -ErrorAction SilentlyContinue } catch { }
try { Remove-Item -Force ($StatusFile + ".tmp") -ErrorAction SilentlyContinue } catch { }
