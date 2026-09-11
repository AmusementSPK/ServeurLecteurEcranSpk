using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8090");

builder.Services.Configure<StreamingOptions>(
    builder.Configuration.GetSection("Streaming"));

const long maxUploadBytes = 2L * 1024 * 1024 * 1024;
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});

builder.Services.AddSingleton<TvConfigStore>();
builder.Services.AddSingleton<HlsProcessManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HlsProcessManager>());

var app = builder.Build();

var cfg = app.Services.GetRequiredService<IOptions<StreamingOptions>>().Value;
var hlsRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, cfg.HlsFolder));
var mediaRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, cfg.MediaFolder));

Directory.CreateDirectory(hlsRoot);
Directory.CreateDirectory(mediaRoot);

app.UseDefaultFiles();
app.UseStaticFiles();

// Flux HLS utilisés par les Roku. La convention d'URL reste inchangée.
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

app.MapGet("/health", (HlsProcessManager manager) => Results.Ok(new
{
    status = "OK",
    mode = "SERVER_SIDE_LOOP_HLS",
    time = DateTimeOffset.Now,
    streams = manager.GetStatus()
}));

// Route de diagnostic compatible avec l'ancien serveur.
// L'application Roku utilise directement /hls/tvX/index.m3u8.
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

app.MapGet("/api/tvs", (TvConfigStore store, HlsProcessManager manager) =>
{
    var items = store.GetAll().Select(tv =>
    {
        var filePath = Path.Combine(mediaRoot, tv.FileName);
        var info = File.Exists(filePath) ? new FileInfo(filePath) : null;

        return new
        {
            id = tv.Id,
            name = tv.Name,
            fileName = tv.FileName,
            hasVideo = info is not null,
            fileSize = info?.Length ?? 0,
            lastModified = info?.LastWriteTime,
            running = manager.IsRunning(tv.Id),
            streamUrl = $"/hls/tv{tv.Id}/index.m3u8"
        };
    });

    return Results.Ok(new
    {
        maxTvCount = cfg.MaxTvCount,
        televisions = items
    });
});

app.MapPost("/api/tvs", (CreateTvRequest request, TvConfigStore store) =>
{
    try
    {
        var tv = store.Create(request.Name);
        return Results.Created($"/api/tvs/{tv.Id}", new
        {
            id = tv.Id,
            name = tv.Name,
            fileName = tv.FileName,
            streamUrl = $"/hls/tv{tv.Id}/index.m3u8"
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
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
        return Results.NotFound(new { error = "Télévision introuvable." });
    }
});

app.MapPost("/api/tvs/{tvId}/video", async (
    string tvId,
    HttpRequest request,
    TvConfigStore store,
    HlsProcessManager manager) =>
{
    var tv = store.Get(tvId);
    if (tv is null)
        return Results.NotFound(new { error = "Télévision introuvable." });

    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Le fichier vidéo est manquant." });

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("video") ?? form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "Le fichier vidéo est vide." });

    if (!Path.GetExtension(file.FileName).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Seuls les fichiers MP4 sont acceptés." });

    var targetName = Path.GetFileName(tv.FileName);
    var targetPath = Path.GetFullPath(Path.Combine(mediaRoot, targetName));

    if (!targetPath.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Chemin vidéo invalide." });

    var tempPath = Path.Combine(mediaRoot, $".upload-{tv.Id}-{Guid.NewGuid():N}.tmp");

    try
    {
        await using (var output = File.Create(tempPath))
        {
            await file.CopyToAsync(output);
        }

        // Arrête seulement cette TV pendant le remplacement du fichier.
        manager.Stop(tv.Id);
        File.Move(tempPath, targetPath, overwrite: true);

        // L'URL HLS ne change jamais; le flux repart avec la nouvelle vidéo.
        manager.Restart(tv.Id);

        var info = new FileInfo(targetPath);
        return Results.Ok(new
        {
            status = "OK",
            tv = tv.Id,
            name = tv.Name,
            fileName = tv.FileName,
            size = info.Length,
            streamUrl = $"/hls/tv{tv.Id}/index.m3u8"
        });
    }
    catch (Exception ex)
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);

        manager.Restart(tv.Id);
        return Results.Problem(
            title: "Impossible de remplacer la vidéo.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run();
