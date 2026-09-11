public sealed class StreamingOptions
{
    public string MediaFolder { get; set; } = "Media";
    public string HlsFolder { get; set; } = "Hls";
    public string DataFolder { get; set; } = "Data";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int SegmentSeconds { get; set; } = 4;
    public int PlaylistSize { get; set; } = 8;

    // Utilisé uniquement pour initialiser Data/tvs.json au premier démarrage.
    // Ensuite, le panneau web conserve la configuration dynamique dans Data/tvs.json.
    public Dictionary<string, string> Tvs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
