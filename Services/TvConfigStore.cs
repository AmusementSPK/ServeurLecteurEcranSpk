using System.Text.Json;
using Microsoft.Extensions.Options;

public sealed class TvConfigStore
{
    private readonly object _gate = new();
    private readonly StreamingOptions _cfg;
    private readonly DisplayServerPaths _paths;
    private readonly ILogger<TvConfigStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private List<TvDefinition> _tvs = new();
    private string _configPath = "";

    public TvConfigStore(
        IOptions<StreamingOptions> options,
        DisplayServerPaths paths,
        ILogger<TvConfigStore> logger)
    {
        _cfg = options.Value;
        _paths = paths;
        _logger = logger;
        Initialize();
    }

    public IReadOnlyList<TvDefinition> GetAll()
    {
        lock (_gate)
        {
            return _tvs
                .OrderBy(x => ParseId(x.Id))
                .Select(Clone)
                .ToArray();
        }
    }

    public TvDefinition? Get(string id)
    {
        lock (_gate)
        {
            var tv = _tvs.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return tv is null ? null : Clone(tv);
        }
    }

    public TvDefinition Create(string? requestedName)
    {
        lock (_gate)
        {
            var usedIds = _tvs
                .Select(x => ParseId(x.Id))
                .Where(x => x > 0 && x < int.MaxValue)
                .ToHashSet();

            var nextId = 1;
            while (usedIds.Contains(nextId))
                nextId++;

            var id = nextId.ToString();
            var tv = new TvDefinition
            {
                Id = id,
                Name = NormalizeName(requestedName, $"TV {id}"),
                FileName = $"tv{id}/tv{id}.mp4"
            };

            _tvs.Add(tv);
            SaveLocked();
            return Clone(tv);
        }
    }

    public TvDefinition Rename(string id, string? requestedName)
    {
        lock (_gate)
        {
            var tv = _tvs.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("Television not found.");

            tv.Name = NormalizeName(requestedName, $"TV {tv.Id}");
            SaveLocked();
            return Clone(tv);
        }
    }

    public TvDefinition SetMediaFile(string id, string relativeFileName)
    {
        lock (_gate)
        {
            var tv = _tvs.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("Television not found.");

            var normalizedPath = NormalizeMediaPath(relativeFileName);
            if (!IsValidMediaPath(normalizedPath))
                throw new InvalidOperationException("Invalid media filename.");

            tv.FileName = normalizedPath;
            SaveLocked();
            return Clone(tv);
        }
    }

    private void Initialize()
    {
        Directory.CreateDirectory(_paths.ConfigRoot);
        _configPath = Path.Combine(_paths.ConfigRoot, "tvs.json");

        lock (_gate)
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    var loaded = JsonSerializer.Deserialize<List<TvDefinition>>(json, _jsonOptions);
                    if (loaded is not null)
                    {
                        _tvs = loaded
                            .Where(IsValid)
                            .Select(Normalize)
                            .OrderBy(x => ParseId(x.Id))
                            .ToList();

                        _logger.LogInformation(
                            "TV configuration loaded from {Path}: {Count} TV.",
                            _configPath,
                            _tvs.Count);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to read {Path}. Resetting from appsettings.json.", _configPath);
                }
            }

            _tvs = _cfg.Tvs
                .OrderBy(x => ParseId(x.Key))
                .Select(x => new TvDefinition
                {
                    Id = x.Key,
                    Name = $"TV {x.Key}",
                    FileName = NormalizeMediaPath(x.Value)
                })
                .Where(IsValid)
                .ToList();

            SaveLocked();
        }
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(_tvs.OrderBy(x => ParseId(x.Id)), _jsonOptions);
        var tempPath = _configPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _configPath, overwrite: true);
    }

    private static TvDefinition Clone(TvDefinition tv) => new()
    {
        Id = tv.Id,
        Name = tv.Name,
        FileName = tv.FileName
    };

    private static bool IsValid(TvDefinition tv)
    {
        return int.TryParse(tv.Id, out var id) &&
               id > 0 &&
               IsValidMediaPath(NormalizeMediaPath(tv.FileName));
    }

    private static TvDefinition Normalize(TvDefinition tv)
    {
        var id = ParseId(tv.Id).ToString();
        return new TvDefinition
        {
            Id = id,
            Name = NormalizeName(tv.Name, $"TV {id}"),
            FileName = NormalizeMediaPath(tv.FileName)
        };
    }

    private static string NormalizeMediaPath(string? value)
    {
        return (value ?? "")
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static bool IsValidMediaPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            return false;

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(x => x is "." or ".."))
            return false;

        if (!Path.GetExtension(parts[^1]).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            return false;

        return parts.All(part => part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    private static string NormalizeName(string? value, string fallback)
    {
        var name = (value ?? "").Trim();
        if (name.Length == 0)
            return fallback;

        return name.Length <= 80 ? name : name[..80];
    }

    private static int ParseId(string? value)
    {
        return int.TryParse(value, out var id) ? id : int.MaxValue;
    }
}
