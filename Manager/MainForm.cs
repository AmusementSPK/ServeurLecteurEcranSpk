using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text.Json;

namespace SPK.Server.Manager;

public sealed class MainForm : Form
{
    private const string ServiceName = "AmusementSPKDisplayServer";
    private const string LocalHealthUrl = "http://127.0.0.1:8090/health";
    private const string LocalWebUrl = "http://127.0.0.1:8090/";

    private static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Amusement SPK",
        "Display Server");

    private readonly Label _serviceValue = new();
    private readonly Label _healthValue = new();
    private readonly Label _ipValue = new();
    private readonly Label _detailValue = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _restartButton = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public MainForm()
    {
        Text = "SPK Server Manager";
        Width = 820;
        Height = 570;
        MinimumSize = new Size(820, 570);
        MaximumSize = new Size(820, 570);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(246, 247, 249);

        BuildUi();

        Shown += async (_, _) => await RefreshStateAsync();
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _http.Dispose();
        };

        _timer.Interval = 5000;
        _timer.Tick += async (_, _) => await RefreshStateAsync();
        _timer.Start();
    }

    private void BuildUi()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = Color.FromArgb(25, 28, 35)
        };
        Controls.Add(header);

        header.Controls.Add(new Label
        {
            Text = "AMUSEMENT SPK",
            ForeColor = Color.FromArgb(255, 112, 70),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Location = new Point(28, 15),
            Size = new Size(300, 24)
        });

        header.Controls.Add(new Label
        {
            Text = "SPK Server Manager",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 21F, FontStyle.Bold),
            Location = new Point(26, 39),
            Size = new Size(700, 42)
        });

        var statusBox = new GroupBox
        {
            Text = "État du serveur",
            Location = new Point(28, 112),
            Size = new Size(746, 150)
        };
        Controls.Add(statusBox);

        AddStatusRow(statusBox, "Service Windows", _serviceValue, 28);
        AddStatusRow(statusBox, "Santé HLS", _healthValue, 63);
        AddStatusRow(statusBox, "Adresse réseau", _ipValue, 98);

        _detailValue.Location = new Point(30, 272);
        _detailValue.Size = new Size(740, 44);
        _detailValue.ForeColor = Color.DimGray;
        Controls.Add(_detailValue);

        ConfigureActionButton(_startButton, "Démarrer", 28, 330, async () => await StartServiceAsync());
        ConfigureActionButton(_stopButton, "Arrêter", 154, 330, async () => await StopServiceAsync());
        ConfigureActionButton(_restartButton, "Redémarrer", 280, 330, async () => await RestartServiceAsync());

        var refresh = NewButton("Actualiser", 406, 330, 118);
        refresh.Click += async (_, _) => await RefreshStateAsync();
        Controls.Add(refresh);

        var web = NewButton("Ouvrir panneau Web", 28, 390, 180);
        web.Click += (_, _) => OpenTarget(LocalWebUrl);
        Controls.Add(web);

        var logs = NewButton("Ouvrir les logs", 220, 390, 160);
        logs.Click += (_, _) => OpenFolder(Path.Combine(DataRoot, "Logs"));
        Controls.Add(logs);

        var data = NewButton("Ouvrir les données", 392, 390, 170);
        data.Click += (_, _) => OpenFolder(DataRoot);
        Controls.Add(data);

        var import = NewButton("Importer ancienne installation", 28, 450, 250);
        import.BackColor = Color.FromArgb(255, 91, 46);
        import.ForeColor = Color.White;
        import.FlatStyle = FlatStyle.Flat;
        import.FlatAppearance.BorderSize = 0;
        import.Click += async (_, _) => await ImportLegacyAsync();
        Controls.Add(import);

        Controls.Add(new Label
        {
            Text = "L'import copie Data et Media d'une ancienne installation vers ProgramData puis redémarre le service.",
            Location = new Point(300, 452),
            Size = new Size(465, 50),
            ForeColor = Color.DimGray
        });
    }

    private static void AddStatusRow(Control parent, string title, Label value, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = title,
            Location = new Point(22, y),
            Size = new Size(180, 28),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        });

        value.Location = new Point(215, y);
        value.Size = new Size(500, 28);
        value.Text = "Vérification...";
        parent.Controls.Add(value);
    }

    private static Button NewButton(string text, int x, int y, int width)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 38)
        };
    }

    private void ConfigureActionButton(Button button, string text, int x, int y, Func<Task> action)
    {
        button.Text = text;
        button.Location = new Point(x, y);
        button.Size = new Size(114, 38);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "SPK Server Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                button.Enabled = true;
                await RefreshStateAsync();
            }
        };
        Controls.Add(button);
    }

    private async Task RefreshStateAsync()
    {
        var status = GetServiceStatus();
        _serviceValue.Text = status?.ToString() ?? "Non installé";
        _serviceValue.ForeColor = status == ServiceControllerStatus.Running
            ? Color.DarkGreen
            : Color.DarkRed;

        _startButton.Enabled = status is ServiceControllerStatus.Stopped or ServiceControllerStatus.Paused;
        _stopButton.Enabled = status == ServiceControllerStatus.Running;
        _restartButton.Enabled = status == ServiceControllerStatus.Running;

        try
        {
            using var response = await _http.GetAsync(LocalHealthUrl);
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;
            var health = root.TryGetProperty("status", out var statusProperty)
                ? statusProperty.GetString() ?? "Inconnu"
                : "Inconnu";

            var healthy = response.IsSuccessStatusCode &&
                          root.TryGetProperty("hlsSupervisorHealthy", out var hlsProperty) &&
                          hlsProperty.GetBoolean();

            _healthValue.Text = healthy ? "OK" : health;
            _healthValue.ForeColor = healthy ? Color.DarkGreen : Color.DarkRed;
        }
        catch
        {
            _healthValue.Text = "Injoignable";
            _healthValue.ForeColor = Color.DarkRed;
        }

        var addresses = GetLanAddresses();
        _ipValue.Text = addresses.Count == 0
            ? "Aucune IPv4 détectée"
            : string.Join("   ", addresses.Select(ip => $"http://{ip}:8090/"));

        _detailValue.Text = $"Données : {DataRoot}";
    }

    private static ServiceControllerStatus? GetServiceStatus()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return controller.Status;
        }
        catch
        {
            return null;
        }
    }

    private static async Task StartServiceAsync()
    {
        using var controller = new ServiceController(ServiceName);
        controller.Refresh();

        if (controller.Status == ServiceControllerStatus.Running)
            return;

        controller.Start();
        await Task.Run(() =>
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)));
    }

    private static async Task StopServiceAsync()
    {
        using var controller = new ServiceController(ServiceName);
        controller.Refresh();

        if (controller.Status == ServiceControllerStatus.Stopped)
            return;

        if (controller.CanStop)
            controller.Stop();

        await Task.Run(() =>
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)));
    }

    private static async Task RestartServiceAsync()
    {
        await StopServiceAsync();
        await Task.Delay(800);
        await StartServiceAsync();
    }

    private async Task ImportLegacyAsync()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = "Choisis le dossier racine de l'ancienne installation ServeurLecteurEcranSpk.",
            ShowNewFolderButton = false
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
            return;

        var sourceRoot = picker.SelectedPath;
        var sourceData = Path.Combine(sourceRoot, "Data");
        var sourceMedia = Path.Combine(sourceRoot, "Media");

        if (!Directory.Exists(sourceData) && !Directory.Exists(sourceMedia))
        {
            MessageBox.Show(
                "Ce dossier ne contient ni Data ni Media. Choisis la racine de l'ancienne installation.",
                "Import SPK",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            "Le service sera arrêté, puis Data et Media seront copiés vers la nouvelle installation. " +
            "Les fichiers portant le même nom seront remplacés. Continuer ?",
            "Importer l'ancienne installation",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirmation != DialogResult.Yes)
            return;

        try
        {
            await StopServiceAsync();

            Directory.CreateDirectory(DataRoot);

            if (Directory.Exists(sourceData))
                CopyDirectory(sourceData, Path.Combine(DataRoot, "Data"));

            if (Directory.Exists(sourceMedia))
                CopyDirectory(sourceMedia, Path.Combine(DataRoot, "Media"));

            await StartServiceAsync();

            MessageBox.Show(
                "Import terminé. Le service SPK a été redémarré.",
                "Import SPK",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            try { await StartServiceAsync(); } catch { }

            MessageBox.Show(
                "L'import a échoué : " + ex.Message,
                "Import SPK",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        await RefreshStateAsync();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(directory));
            CopyDirectory(directory, target);
        }
    }

    private static List<string> GetLanAddresses()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                .Select(ip => ip.ToString())
                .Distinct()
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void OpenTarget(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $""{path}"",
            UseShellExecute = true
        });
    }
}
