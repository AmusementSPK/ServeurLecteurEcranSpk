using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

public sealed class SpkPaths
{
    public string ProgramRoot { get; }
    public string DataRoot { get; }
    public string MediaRoot { get; }
    public string HlsRoot { get; }
    public string ConfigRoot { get; }
    public string LogsRoot { get; }
    public string FfmpegPath { get; }

    public SpkPaths(
        IHostEnvironment environment,
        IOptions<StreamingOptions> options,
        IConfiguration configuration)
    {
        ProgramRoot = Path.GetFullPath(AppContext.BaseDirectory);

        var configuredRoot =
            configuration["Spk:DataRoot"] ??
            Environment.GetEnvironmentVariable("SPK_DATA_ROOT");

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            DataRoot = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configuredRoot));
        }
        else if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
        {
            DataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Amusement SPK",
                "Display Server");
        }
        else
        {
            // Mode développement / lancement manuel depuis le repo.
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
