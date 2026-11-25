namespace GameTranslatorIT.Core.Models;

/// <summary>
/// Regione di interesse in coordinate relative (0..1) rispetto al frame catturato.
/// </summary>
public sealed record SubtitleRegion(double Left, double Top, double Width, double Height)
{
    public static SubtitleRegion Auto => new(0, 0.68, 1, 0.30);

    public (int X, int Y, int W, int H) ToPixels(int frameWidth, int frameHeight)
    {
        var x = (int)Math.Clamp(Math.Round(Left * frameWidth), 0, frameWidth - 1);
        var y = (int)Math.Clamp(Math.Round(Top * frameHeight), 0, frameHeight - 1);
        var w = (int)Math.Clamp(Math.Round(Width * frameWidth), 1, frameWidth - x);
        var h = (int)Math.Clamp(Math.Round(Height * frameHeight), 1, frameHeight - y);
        return (x, y, w, h);
    }
}

public sealed record OcrTextResult(
    string Text,
    IReadOnlyList<string> Lines,
    DateTimeOffset DetectedAt);
