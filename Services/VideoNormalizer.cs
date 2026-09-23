using System.Diagnostics;
public sealed class VideoNormalizer
{
    private readonly DisplayServerPaths _paths;
    private readonly ILogger<VideoNormalizer> _logger;

    public VideoNormalizer(
        DisplayServerPaths paths,
        ILogger<VideoNormalizer> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task NormalizeAsync(
        string inputPath,
        string outputPath,
        bool isStillImage,
        CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        var psi = new ProcessStartInfo
        {
            FileName = _paths.FfmpegPath,
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

        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:v:0");

        if (!isStillImage)
        {
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add("0:a?");
        }

        psi.ArgumentList.Add("-vf");
        if (isStillImage)
        {
            // Always use the first frame, even for animated GIF/WebP files.
            // The frame is cloned for exactly 10 seconds.
            psi.ArgumentList.Add("select='eq(n,0)',scale='min(1920,iw)':'min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2,tpad=stop_mode=clone:stop_duration=10,fps=30");
        }
        else
        {
            // Videos: max 1080p at a constant 30 fps for stable HLS playback.
            psi.ArgumentList.Add("scale='min(1920,iw)':'min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2,fps=30");
        }

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

        if (isStillImage)
        {
            psi.ArgumentList.Add("-an");
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add("10");
        }
        else
        {
            psi.ArgumentList.Add("-c:a");
            psi.ArgumentList.Add("aac");
            psi.ArgumentList.Add("-b:a");
            psi.ArgumentList.Add("160k");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add("48000");
            psi.ArgumentList.Add("-ac");
            psi.ArgumentList.Add("2");
        }

        psi.ArgumentList.Add("-movflags");
        psi.ArgumentList.Add("+faststart");
        psi.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = psi };

        _logger.LogInformation(
            isStillImage
                ? "Still-image conversion (10 s): {Input} -> {Output}"
                : "Video normalization: {Input} -> {Output}",
            Path.GetFileName(inputPath),
            Path.GetFileName(outputPath));

        if (!process.Start())
            throw new InvalidOperationException("FFmpeg could not start.");

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
                ? $"FFmpeg exited with code {process.ExitCode}."
                : stderr.Trim();

            throw new InvalidOperationException($"Media conversion failed: {detail}");
        }

        _logger.LogInformation("Media conversion completed.");
    }
}
