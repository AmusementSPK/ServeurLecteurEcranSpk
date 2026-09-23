using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

public sealed class DisplayServerPaths
{
    public string ProgramRoot { get; }
    public string DataRoot { get; }
    public string MediaRoot { get; }
    public string HlsRoot { get; }
    public string ConfigRoot { get; }
    public string LogsRoot { get; }
    public string FfmpegPath { get; }

    public DisplayServerPaths(
        IHostEnvironment environment,
        IOptions<StreamingOptions> options,
        IConfiguration configuration)
    {
        ProgramRoot = Path.GetFullPath(AppContext.BaseDirectory);

        var configuredRoot =
            configuration["DisplayServer:DataRoot"] ??
            Environment.GetEnvironmentVariable("DISPLAY_SERVER_DATA_ROOT");

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredRoot);
            DataRoot = Path.IsPathRooted(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(ProgramRoot, expanded));
        }
        else if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
        {
            DataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Local Display Server");
        }
        else
        {
            DataRoot = environment.ContentRootPath;
        }

        var cfg = options.Value;
        MediaRoot = ResolveDataFolder(cfg.MediaFolder, "Media");
        HlsRoot = ResolveDataFolder(cfg.HlsFolder, "Hls");
        ConfigRoot = ResolveDataFolder(cfg.DataFolder, "Data");
        LogsRoot = Path.Combine(DataRoot, "Logs");

        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(MediaRoot);
        Directory.CreateDirectory(HlsRoot);
        Directory.CreateDirectory(ConfigRoot);
        Directory.CreateDirectory(LogsRoot);

        FfmpegPath = ResolveFfmpegPath(cfg.FfmpegPath);
    }

    private string ResolveDataFolder(string? configured, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        if (Path.IsPathRooted(value))
            return Path.GetFullPath(value);
        return Path.GetFullPath(Path.Combine(DataRoot, value));
    }

    private string ResolveFfmpegPath(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine("Tools", "ffmpeg.exe")
            : configured.Trim();

        if (Path.IsPathRooted(value))
            return value;

        var bundled = Path.GetFullPath(Path.Combine(ProgramRoot, value));
        return File.Exists(bundled) ? bundled : "ffmpeg";
    }
}