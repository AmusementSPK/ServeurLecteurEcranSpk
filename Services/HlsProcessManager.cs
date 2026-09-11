using System.Diagnostics;
using Microsoft.Extensions.Options;

public sealed class HlsProcessManager : BackgroundService
{
    private readonly ILogger<HlsProcessManager> _logger;
    private readonly StreamingOptions _cfg;
    private readonly IHostEnvironment _environment;
    private readonly TvConfigStore _store;
    private readonly Dictionary<string, Process> _processes =
        new(StringComparer.OrdinalIgnoreCase);

    public HlsProcessManager(
        ILogger<HlsProcessManager> logger,
        IOptions<StreamingOptions> options,
        IHostEnvironment environment,
        TvConfigStore store)
    {
        _logger = logger;
        _cfg = options.Value;
        _environment = environment;
        _store = store;
    }

    public bool IsRunning(string tvId)
    {
        lock (_processes)
        {
            return _processes.TryGetValue(tvId, out var p) && !p.HasExited;
        }
    }

    public void Stop(string tvId)
    {
        StopProcess(tvId);
    }

    public void Restart(string tvId)
    {
        StopProcess(tvId);
        var tv = _store.Get(tvId);
        if (tv is not null)
            TryStart(tv);
    }

    public object GetStatus()
    {
        var mediaRoot = GetMediaRoot();

        return _store.GetAll()
            .Select(tv =>
            {
                var path = Path.Combine(mediaRoot, tv.FileName);
                return new
                {
                    tv = tv.Id,
                    name = tv.Name,
                    file = tv.FileName,
                    hasVideo = File.Exists(path),
                    running = IsRunning(tv.Id),
                    stream = $"/hls/tv{tv.Id}/index.m3u8"
                };
            })
            .ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SPK HLS: démarrage du gestionnaire FFmpeg.");
        EnsureConfiguredStreams();

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            EnsureConfiguredStreams();
        }
    }

    private void EnsureConfiguredStreams()
    {
        var mediaRoot = GetMediaRoot();

        foreach (var tv in _store.GetAll())
        {
            var input = Path.Combine(mediaRoot, tv.FileName);
            if (!File.Exists(input))
                continue;

            if (!IsRunning(tv.Id))
                TryStart(tv);
        }
    }

    private void TryStart(TvDefinition tv)
    {
        try
        {
            if (IsRunning(tv.Id))
                return;

            var mediaRoot = GetMediaRoot();
            var hlsRoot = GetHlsRoot();
            Directory.CreateDirectory(mediaRoot);
            Directory.CreateDirectory(hlsRoot);

            var input = Path.GetFullPath(Path.Combine(mediaRoot, tv.FileName));
            if (!input.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Chemin invalide pour TV {TvId}.", tv.Id);
                return;
            }

            if (!File.Exists(input))
                return;

            var outputDir = Path.Combine(hlsRoot, $"tv{tv.Id}");
            if (Directory.Exists(outputDir))
            {
                try
                {
                    Directory.Delete(outputDir, recursive: true);
                }
                catch
                {
                    // Windows peut garder brièvement un segment ouvert.
                }
            }

            Directory.CreateDirectory(outputDir);

            var manifest = Path.Combine(outputDir, "index.m3u8");
            var segmentPattern = Path.Combine(outputDir, "segment_%09d.ts");

            var psi = new ProcessStartInfo
            {
                FileName = _cfg.FfmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            // Système global conservé : boucle côté serveur, flux HLS live vers Roku.
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("warning");
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-re");
            psi.ArgumentList.Add("-stream_loop");
            psi.ArgumentList.Add("-1");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(input);

            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:v:0");
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:a?");
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("copy");
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("copy");

            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("hls");
            psi.ArgumentList.Add("-hls_time");
            psi.ArgumentList.Add(Math.Max(2, _cfg.SegmentSeconds).ToString());
            psi.ArgumentList.Add("-hls_list_size");
            psi.ArgumentList.Add(Math.Max(4, _cfg.PlaylistSize).ToString());
            psi.ArgumentList.Add("-hls_flags");
            psi.ArgumentList.Add("delete_segments+omit_endlist+independent_segments");
            psi.ArgumentList.Add("-hls_segment_filename");
            psi.ArgumentList.Add(segmentPattern);
            psi.ArgumentList.Add(manifest);

            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogWarning("FFmpeg TV {TvId}: {Message}", tv.Id, e.Data);
            };

            process.Exited += (_, _) =>
            {
                _logger.LogWarning(
                    "FFmpeg TV {TvId} s'est arrêté (code {ExitCode}).",
                    tv.Id,
                    SafeExitCode(process));
            };

            if (!process.Start())
            {
                _logger.LogError("Impossible de démarrer FFmpeg pour TV {TvId}.", tv.Id);
                return;
            }

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            lock (_processes)
            {
                _processes[tv.Id] = process;
            }

            _logger.LogInformation(
                "TV {TvId}: flux HLS démarré -> /hls/tv{TvId}/index.m3u8",
                tv.Id,
                tv.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TV {TvId}: impossible de démarrer FFmpeg. Vérifie que FFmpeg est installé.",
                tv.Id);
        }
    }

    private string GetMediaRoot() => Path.GetFullPath(
        Path.Combine(_environment.ContentRootPath, _cfg.MediaFolder));

    private string GetHlsRoot() => Path.GetFullPath(
        Path.Combine(_environment.ContentRootPath, _cfg.HlsFolder));

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private void StopProcess(string tvId)
    {
        Process? process = null;

        lock (_processes)
        {
            if (_processes.TryGetValue(tvId, out process))
                _processes.Remove(tvId);
        }

        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
        }

        process.Dispose();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        string[] ids;
        lock (_processes)
        {
            ids = _processes.Keys.ToArray();
        }

        foreach (var id in ids)
            StopProcess(id);

        await base.StopAsync(cancellationToken);
    }
}
