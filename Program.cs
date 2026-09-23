using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var serverUrls = builder.Configuration["Server:Urls"]
    ?? Environment.GetEnvironmentVariable("DISPLAY_SERVER_URLS")
    ?? "http://0.0.0.0:8090";
var displayName = builder.Configuration["Branding:Name"] ?? "Local Display Server";
var tagline = builder.Configuration["Branding:Tagline"] ?? "Self-hosted media signage";
var serviceDisplayName = builder.Configuration["Branding:ServiceDisplayName"] ?? "Local Display Server";

builder.WebHost.UseUrls(serverUrls);

builder.Services.Configure<StreamingOptions>(
    builder.Configuration.GetSection("Streaming"));

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = serviceDisplayName;
});

builder.Services.Configure<HostOptions>(options =>
{
    // En service Windows, un BackgroundService réellement fatal doit arrêter
    // le processus afin que les Recovery Actions du Service Control Manager
    // puissent redémarrer l'application proprement.
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

const long maxUploadBytes = 2L * 1024 * 1024 * 1024;
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});

builder.Services.AddSingleton<DisplayServerPaths>();
builder.Services.AddSingleton<ILoggerProvider, DailyFileLoggerProvider>();
builder.Services.AddSingleton<TvConfigStore>();
builder.Services.AddSingleton<HlsProcessManager>();
builder.Services.AddSingleton<VideoNormalizer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HlsProcessManager>());
builder.Services.AddHostedService<ServiceRecoveryMonitor>();

var app = builder.Build();

var paths = app.Services.GetRequiredService<DisplayServerPaths>();
var hlsRoot = paths.HlsRoot;
var mediaRoot = paths.MediaRoot;

Directory.CreateDirectory(hlsRoot);
Directory.CreateDirectory(mediaRoot);

string ResolveMediaPath(string relativePath)
{
    var normalizedRelative = (relativePath ?? "").Replace('/', Path.DirectorySeparatorChar);
    var fullPath = Path.GetFullPath(Path.Combine(mediaRoot, normalizedRelative));
    var rootWithSeparator = mediaRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Invalid media path.");

    return fullPath;
}

string MakeSafeBaseName(string originalFileName)
{
    var incomingName = (originalFileName ?? "")
        .Replace('\\', '/')
        .Split('/', StringSplitOptions.RemoveEmptyEntries)
        .LastOrDefault() ?? "media";

    var baseName = Path.GetFileNameWithoutExtension(incomingName).Trim();
    var invalid = Path.GetInvalidFileNameChars().ToHashSet();
    var builderName = new StringBuilder();

    foreach (var ch in baseName)
    {
        if (!invalid.Contains(ch) && ch != '/' && ch != '\\')
            builderName.Append(ch);
    }

    var result = builderName.ToString().Trim().TrimEnd('.', ' ');
    if (string.IsNullOrWhiteSpace(result))
        result = "media";

    if (result.Length > 120)
        result = result[..120].TrimEnd('.', ' ');

    return result;
}

app.UseDefaultFiles();
app.UseStaticFiles();

// HLS streams consumed by browsers, Roku, VIDAA and other compatible clients.
var hlsContentTypes = new FileExtensionContentTypeProvider();
hlsContentTypes.Mappings[".m3u8"] = "application/vnd.apple.mpegurl";
hlsContentTypes.Mappings[".ts"] = "video/mp2t";

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(hlsRoot),
    RequestPath = "/hls",
    ContentTypeProvider = hlsContentTypes,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        ctx.Context.Response.Headers.Pragma = "no-cache";
        ctx.Context.Response.Headers.Expires = "0";
        ctx.Context.Response.Headers.AccessControlAllowOrigin = "*";
    }
});

app.MapGet("/health", (HlsProcessManager manager) =>
{
    var healthy = manager.IsSupervisorHealthy;
    var payload = new
    {
        status = healthy ? "OK" : "DEGRADED",
        mode = "SERVER_SIDE_LOOP_HLS",
        time = DateTimeOffset.Now,
        hlsSupervisorHealthy = healthy,
        hlsLastMaintenanceUtc = manager.LastMaintenanceUtc,
        streams = manager.GetStatus()
    };

    return healthy
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/system", (DisplayServerPaths systemPaths) => Results.Ok(new
{
    name = displayName,
    tagline,
    service = serviceDisplayName,
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
    urls = serverUrls,
    dataRoot = systemPaths.DataRoot,
    mediaRoot = systemPaths.MediaRoot,
    logsRoot = systemPaths.LogsRoot,
    ffmpeg = systemPaths.FfmpegPath
}));

app.MapGet("/tv/{tvId}", (string tvId, TvConfigStore store) =>
{
    var tv = store.Get(tvId);
    if (tv is null)
        return Results.NotFound(new { error = "TV_NOT_CONFIGURED", tv = tvId });

    return Results.Ok(new
    {
        tv = tv.Id,
        name = tv.Name,
        type = "hls",
        serverSideLoop = true,
        stream = $"/hls/tv{tv.Id}/index.m3u8"
    });
});

object ClientTelevisionList(TvConfigStore store)
{
    var televisions = store.GetAll().Select(tv => new
    {
        id = tv.Id,
        name = tv.Name,
        streamUrl = $"/hls/tv{tv.Id}/index.m3u8"
    });

    return new { televisions };
}

app.MapGet("/api/clients/tvs", (TvConfigStore store) => Results.Ok(ClientTelevisionList(store)));

// Compatibility endpoint for simple Roku clients.
app.MapGet("/api/roku/tvs", (TvConfigStore store) => Results.Ok(ClientTelevisionList(store)));

app.MapGet("/api/tvs", (TvConfigStore store, HlsProcessManager manager) =>
{
    var items = store.GetAll().Select(tv =>
    {
        string filePath;
        try
        {
            filePath = ResolveMediaPath(tv.FileName);
        }
        catch
        {
            filePath = "";
        }

        var info = !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)
            ? new FileInfo(filePath)
            : null;

        return new
        {
            id = tv.Id,
            name = tv.Name,
            fileName = Path.GetFileName(tv.FileName),
            hasVideo = info is not null,
            fileSize = info?.Length ?? 0,
            lastModified = info?.LastWriteTime,
            running = manager.IsRunning(tv.Id),
            streamUrl = $"/hls/tv{tv.Id}/index.m3u8"
        };
    });

    return Results.Ok(new { televisions = items });
});

app.MapPost("/api/tvs", (CreateTvRequest request, TvConfigStore store) =>
{
    var tv = store.Create(request.Name);
    return Results.Created($"/api/tvs/{tv.Id}", new
    {
        id = tv.Id,
        name = tv.Name,
        fileName = Path.GetFileName(tv.FileName),
        streamUrl = $"/hls/tv{tv.Id}/index.m3u8"
    });
});

app.MapPut("/api/tvs/{tvId}/name", (string tvId, RenameTvRequest request, TvConfigStore store) =>
{
    try
    {
        var tv = store.Rename(tvId, request.Name);
        return Results.Ok(new { id = tv.Id, name = tv.Name });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Television not found." });
    }
});

app.MapPost("/api/tvs/{tvId}/video", async (
    string tvId,
    HttpRequest request,
    TvConfigStore store,
    HlsProcessManager manager,
    VideoNormalizer normalizer) =>
{
    var tv = store.Get(tvId);
    if (tv is null)
        return Results.NotFound(new { error = "Television not found." });

    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Media file is missing." });

    var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
    var file = form.Files.GetFile("video") ?? form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "Media file is empty." });

    var originalFileName = file.FileName
        .Replace('\\', '/')
        .Split('/', StringSplitOptions.RemoveEmptyEntries)
        .LastOrDefault() ?? "media";

    var extension = Path.GetExtension(originalFileName).ToLowerInvariant();

    var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".mpeg", ".mpg",
        ".wmv", ".flv", ".mts", ".m2ts", ".3gp"
    };

    var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff",
        ".avif", ".heic", ".heif", ".jfif"
    };

    var isImage = imageExtensions.Contains(extension) ||
                  (file.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false);
    var isVideo = videoExtensions.Contains(extension) ||
                  (file.ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ?? false);

    if (!isImage && !isVideo)
    {
        return Results.BadRequest(new
        {
            error = "Unsupported format. Upload a standard video or image (JPG, PNG, WEBP, GIF, TIFF, AVIF, HEIC, etc.)."
        });
    }

    var safeBaseName = MakeSafeBaseName(originalFileName);
    var outputFileName = safeBaseName + ".mp4";
    var relativeTargetPath = $"tv{tv.Id}/{outputFileName}";
    var targetPath = ResolveMediaPath(relativeTargetPath);
    var targetDirectory = Path.GetDirectoryName(targetPath)!;
    Directory.CreateDirectory(targetDirectory);

    var oldMediaPath = ResolveMediaPath(tv.FileName);
    var incomingDirectory = Path.Combine(mediaRoot, ".incoming");
    Directory.CreateDirectory(incomingDirectory);

    var tempExtension = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension;
    var uploadTempPath = Path.Combine(
        incomingDirectory,
        $"upload-{tv.Id}-{Guid.NewGuid():N}{tempExtension}");
    var normalizedTempPath = Path.Combine(
        targetDirectory,
        $".normalized-{Guid.NewGuid():N}.mp4");

    var streamWasStopped = false;

    try
    {
        await using (var output = File.Create(uploadTempPath))
        {
            await file.CopyToAsync(output, request.HttpContext.RequestAborted);
        }

        // Vidéo : normalisation H.264/AAC 30 fps.
        // Image : conversion en MP4 H.264 de 10 secondes, image fixe.
        await normalizer.NormalizeAsync(
            uploadTempPath,
            normalizedTempPath,
            isImage,
            request.HttpContext.RequestAborted);

        manager.Stop(tv.Id);
        streamWasStopped = true;

        File.Move(normalizedTempPath, targetPath, overwrite: true);
        store.SetMediaFile(tv.Id, relativeTargetPath);

        if (!oldMediaPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldMediaPath))
        {
            try { File.Delete(oldMediaPath); } catch { }
        }

        manager.Restart(tv.Id);
        streamWasStopped = false;

        var info = new FileInfo(targetPath);
        return Results.Ok(new
        {
            status = "OK",
            tv = tv.Id,
            name = tv.Name,
            fileName = outputFileName,
            originalFileName,
            sourceType = isImage ? "image" : "video",
            imageDurationSeconds = isImage ? 10 : (int?)null,
            size = info.Length,
            normalized = true,
            streamUrl = $"/hls/tv{tv.Id}/index.m3u8"
        });
    }
    catch (OperationCanceledException)
    {
        if (streamWasStopped)
            manager.Restart(tv.Id);

        return Results.Problem(
            title: "Upload cancelled.",
            statusCode: StatusCodes.Status499ClientClosedRequest);
    }
    catch (Exception ex)
    {
        if (streamWasStopped)
            manager.Restart(tv.Id);

        return Results.Problem(
            title: "Unable to prepare media.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
    finally
    {
        try
        {
            if (File.Exists(uploadTempPath))
                File.Delete(uploadTempPath);
        }
        catch { }

        try
        {
            if (File.Exists(normalizedTempPath))
                File.Delete(normalizedTempPath);
        }
        catch { }
    }
});

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DisplayServer.Server");
app.Lifetime.ApplicationStarted.Register(() =>
{
    startupLogger.LogInformation(
        "Display server started. DataRoot={DataRoot}; FFmpeg={FfmpegPath}; URLs={Urls}",
        paths.DataRoot,
        paths.FfmpegPath,
        serverUrls);
});

app.Run();
