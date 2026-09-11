public sealed class TvDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
}

public sealed record CreateTvRequest(string? Name);
public sealed record RenameTvRequest(string? Name);
