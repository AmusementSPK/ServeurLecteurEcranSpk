using System.Diagnostics;
using Microsoft.Extensions.Options;

public sealed class VideoNormalizer
{
    private readonly StreamingOptions _cfg;
    private readonly ILogger<VideoNormalizer> _logger;

    public VideoNormalizer(
        IOptions<StreamingOptions> options,
        ILogger<VideoNormalizer> logger)
    {
        _cfg = options.Value;
        _logger = logger;
    }

    public async Task NormalizeAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        var psi = new ProcessStartInfo
        {
            FileName = _cfg.FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputPath);

        // Une seule piste vidéo, audio facultatif.
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:v:0");
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:a?");

        // Format de référence SPK : 1080p max, 30 fps constant, H.264 très compatible.
        // Les keyframes toutes les 2 secondes rendent les segments HLS propres et fluides.
        psi.ArgumentList.Add("-vf");
        psi.ArgumentList.Add("scale='min(1920,iw)':-2:force_original_aspect_ratio=decrease,fps=30");
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-preset");
        psi.ArgumentList.Add("veryfast");
        psi.ArgumentList.Add("-crf");
        psi.ArgumentList.Add("20");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-profile:v");
        psi.ArgumentList.Add("high");
        psi.ArgumentList.Add("-level:v");
        psi.ArgumentList.Add("4.1");
        psi.ArgumentList.Add("-g");
        psi.ArgumentList.Add("60");
        psi.ArgumentList.Add("-keyint_min");
        psi.ArgumentList.Add("60");
        psi.ArgumentList.Add("-sc_threshold");
        psi.ArgumentList.Add("0");

        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("160k");
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add("48000");
        psi.ArgumentList.Add("-ac");
        psi.ArgumentList.Add("2");

        psi.ArgumentList.Add("-movflags");
        psi.ArgumentList.Add("+faststart");
        psi.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = psi };

        _logger.LogInformation(
            "Normalisation vidéo: {Input} -> {Output}",
            Path.GetFileName(inputPath),
            Path.GetFileName(outputPath));

        if (!process.Start())
            throw new InvalidOperationException("FFmpeg n'a pas pu démarrer.");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;
        await stdoutTask;

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            var detail = string.IsNullOrWhiteSpace(stderr)
                ? $"FFmpeg a terminé avec le code {process.ExitCode}."
                : stderr.Trim();

            throw new InvalidOperationException($"Conversion vidéo impossible: {detail}");
        }

        _logger.LogInformation("Normalisation vidéo terminée.");
    }
}
