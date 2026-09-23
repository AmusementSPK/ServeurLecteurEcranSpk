using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text.Json;
using System.Security.Principal;

namespace DisplayServer.Manager;

public sealed class MainForm : Form
{
    private const string ServiceName = "LocalDisplayServer";
    private static readonly int ServerPort = LoadServerPort();
    private static readonly string LocalHealthUrl = $"http://127.0.0.1:{ServerPort}/health";
    private static readonly string LocalWebUrl = $"http://127.0.0.1:{ServerPort}/";
    private static readonly string DataRoot = LoadDataRoot();

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
        Text = "Display Server Manager";
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
            Text = "LOCAL DISPLAY SERVER",
            ForeColor = Color.FromArgb(255, 112, 70),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Location = new Point(28, 15),
            Size = new Size(300, 24)
        });

        header.Controls.Add(new Label
        {
            Text = "Display Server Manager",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 21F, FontStyle.Bold),
            Location = new Point(26, 39),
            Size = new Size(700, 42)
        });

        var statusBox = new GroupBox
        {
            Text = "Server status",
            Location = new Point(28, 112),
            Size = new Size(746, 150)
        };
        Controls.Add(statusBox);

        AddStatusRow(statusBox, "Windows service", _serviceValue, 28);
        AddStatusRow(statusBox, "HLS health", _healthValue, 63);
        AddStatusRow(statusBox, "Network address", _ipValue, 98);

        _detailValue.Location = new Point(30, 272);
        _detailValue.Size = new Size(740, 44);
        _detailValue.ForeColor = Color.DimGray;
        Controls.Add(_detailValue);

        ConfigureActionButton(_startButton, "Start", 28, 330, async () => await StartServiceAsync());
        ConfigureActionButton(_stopButton, "Stop", 154, 330, async () => await StopServiceAsync());
        ConfigureActionButton(_restartButton, "Restart", 280, 330, async () => await RestartServiceAsync());

        var refresh = NewButton("Refresh", 406, 330, 118);
        refresh.Click += async (_, _) => await RefreshStateAsync();
        Controls.Add(refresh);

        var web = NewButton("Open web panel", 28, 390, 180);
        web.Click += (_, _) => OpenTarget(LocalWebUrl);
        Controls.Add(web);

        var logs = NewButton("Open logs", 220, 390, 160);
        logs.Click += (_, _) => OpenFolder(Path.Combine(DataRoot, "Logs"));
        Controls.Add(logs);

        var data = NewButton("Open data", 392, 390, 170);
        data.Click += (_, _) => OpenFolder(DataRoot);
        Controls.Add(data);

        var import = NewButton("Import existing data", 28, 450, 250);
        import.BackColor = Color.FromArgb(255, 91, 46);
        import.ForeColor = Color.White;
        import.FlatStyle = FlatStyle.Flat;
        import.FlatAppearance.BorderSize = 0;
        import.Click += async (_, _) => await ImportLegacyAsync();
        Controls.Add(import);

        Controls.Add(new Label
        {
            Text = "Imports Data and Media from another installation into ProgramData, then restarts the service.",
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
        value.Text = "Checking...";
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
            if (!EnsureAdministrator())
                return;

            button.Enabled = false;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Display Server Manager",
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
        _serviceValue.Text = status?.ToString() ?? "Not installed";
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
                ? statusProperty.GetString() ?? "Unknown"
                : "Unknown";

            var healthy = response.IsSuccessStatusCode &&
                          root.TryGetProperty("hlsSupervisorHealthy", out var hlsProperty) &&
                          hlsProperty.GetBoolean();

            _healthValue.Text = healthy ? "OK" : health;
            _healthValue.ForeColor = healthy ? Color.DarkGreen : Color.DarkRed;
        }
        catch
        {
            _healthValue.Text = "Unreachable";
            _healthValue.ForeColor = Color.DarkRed;
        }

        var addresses = GetLanAddresses();
        _ipValue.Text = addresses.Count == 0
            ? "No IPv4 address detected"
            : string.Join("   ", addresses.Select(ip => $"http://{ip}:8090/"));

        _detailValue.Text = $"Data: {DataRoot}";
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
        if (!EnsureAdministrator())
            return;

        using var picker = new FolderBrowserDialog
        {
            Description = "Choose the root folder of an existing display-server installation.",
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
                "This folder contains neither Data nor Media. Choose the installation root.",
                "Import data",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            "Le service sera arrêté, puis Data et Media seront copiés vers la nouvelle installation. " +
            "Les fichiers portant le même nom seront remplacés. Continuer ?",
            "Import existing data",
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
                "Import completed. The display service was restarted.",
                "Import data",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            try { await StartServiceAsync(); } catch { }

            MessageBox.Show(
                "Import failed: " + ex.Message,
                "Import data",
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


    private bool EnsureAdministrator()
    {
        if (IsAdministrator())
            return true;

        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                throw new InvalidOperationException("Chemin du Manager introuvable.");

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas"
            });

            BeginInvoke(Close);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // L'utilisateur a annulé la demande UAC.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Unable to obtain administrator rights: " + ex.Message,
                "Display Server Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        return false;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }


    private static string InstallRoot
    {
        get
        {
            var managerRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Directory.GetParent(managerRoot)?.FullName ?? AppContext.BaseDirectory;
        }
    }

    private static int LoadServerPort()
    {
        try
        {
            var configPath = Path.Combine(InstallRoot, "appsettings.json");
            if (!File.Exists(configPath))
                return 8090;

            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("Server", out var server) ||
                !server.TryGetProperty("Urls", out var urlsProperty))
                return 8090;

            var urls = urlsProperty.GetString();
            if (string.IsNullOrWhiteSpace(urls))
                return 8090;

            foreach (var candidate in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = candidate
                    .Replace("0.0.0.0", "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    .Replace("*", "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    .Replace("+", "127.0.0.1", StringComparison.OrdinalIgnoreCase);

                if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                    return uri.Port;
            }
        }
        catch
        {
        }

        return 8090;
    }

    private static string LoadDataRoot()
    {
        try
        {
            var configPath = Path.Combine(InstallRoot, "appsettings.json");
            if (File.Exists(configPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(configPath));
                if (document.RootElement.TryGetProperty("DisplayServer", out var section) &&
                    section.TryGetProperty("DataRoot", out var valueProperty))
                {
                    var value = valueProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var expanded = Environment.ExpandEnvironmentVariables(value);
                        return Path.IsPathRooted(expanded)
                            ? Path.GetFullPath(expanded)
                            : Path.GetFullPath(Path.Combine(InstallRoot, expanded));
                    }
                }
            }
        }
        catch
        {
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Local Display Server");
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
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }
}
