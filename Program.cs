using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8090");
builder.Services.Configure<StreamingOptions>(
    builder.Configuration.GetSection("Streaming"));

var app = builder.Build();

app.Use(async (context, next) =>
{
    // Important pour les menus TV : ne pas conserver une ancienne version en cache.
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";
    context.Response.Headers.AccessControlAllowOrigin = "*";
    await next();
});

app.MapGet("/", (IOptions<StreamingOptions> options) =>
{
    var cfg = options.Value;
    var items = cfg.Tvs
        .OrderBy(x => x.Key)
        .Select(x => new
        {
            tv = x.Key,
            file = x.Value,
            streamUrl = $"/tv/{x.Key}"
        });

    return Results.Ok(new
    {
        service = "SPK Roku Streaming Server",
        status = "OK",
        port = 8090,
        mediaFolder = cfg.MediaFolder,
        televisions = items
    });
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "OK",
    time = DateTimeOffset.Now
}));

app.MapGet("/tv/{tvId}", (
    string tvId,
    IOptions<StreamingOptions> options,
    HttpContext context) =>
{
    var cfg = options.Value;

    if (!cfg.Tvs.TryGetValue(tvId, out var fileName) ||
        string.IsNullOrWhiteSpace(fileName))
    {
        return Results.NotFound(new
        {
            error = "TV_NOT_CONFIGURED",
            tv = tvId
        });
    }

    var mediaRoot = Path.GetFullPath(
        Path.Combine(app.Environment.ContentRootPath, cfg.MediaFolder));

    var fullPath = Path.GetFullPath(
        Path.Combine(mediaRoot, fileName));

    // Empêche toute sortie du dossier Media par un chemin du type ../
    if (!fullPath.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "INVALID_FILE_PATH" });
    }

    if (!File.Exists(fullPath))
    {
        return Results.NotFound(new
        {
            error = "VIDEO_NOT_FOUND",
            tv = tvId,
            file = fileName
        });
    }

    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(fullPath, out var contentType))
        contentType = "application/octet-stream";

    context.Response.Headers["X-SPK-TV"] = tvId;
    context.Response.Headers["X-SPK-File"] = Path.GetFileName(fullPath);

    // enableRangeProcessing=true est important pour Roku / seek / streaming HTTP.
    return Results.File(
        fullPath,
        contentType: contentType,
        enableRangeProcessing: true);
});

app.Run();

public sealed class StreamingOptions
{
    public string MediaFolder { get; set; } = "Media";
    public Dictionary<string, string> Tvs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
