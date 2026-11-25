namespace GameTranslatorIT.App.Services;

public sealed class SubtitleEventArgs : EventArgs
{
    public SubtitleEventArgs(string text, DateTimeOffset detectedAt, string regionId, string regionName)
    {
        Text = text;
        DetectedAt = detectedAt;
        RegionId = regionId;
        RegionName = regionName;
    }

    public string Text { get; }
    public DateTimeOffset DetectedAt { get; }
    public string RegionId { get; }
    public string RegionName { get; }
}

public sealed class TranslationEventArgs : EventArgs
{
    public TranslationEventArgs(string source, string translated, string regionId, string regionName)
    {
        Source = source;
        Translated = translated;
        RegionId = regionId;
        RegionName = regionName;
    }

    public string Source { get; }
    public string Translated { get; }
    public string RegionId { get; }
    public string RegionName { get; }
}
