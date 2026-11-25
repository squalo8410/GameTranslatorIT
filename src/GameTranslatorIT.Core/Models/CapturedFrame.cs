namespace GameTranslatorIT.Core.Models;

/// <summary>
/// Un frame reale catturato dallo schermo, in formato BGRA8 (4 byte per pixel).
/// </summary>
public sealed record CapturedFrame(
    byte[] Pixels,
    int Width,
    int Height,
    ScreenRect Bounds,
    ulong Hash,
    DateTimeOffset CapturedAt)
{
    /// <summary>
    /// Hash rapido di una singola regione, campionando 1 riga ogni 4 e 1 pixel ogni 4.
    /// Evita di avviare l'OCR quando cambia solo una zona non selezionata.
    /// </summary>
    public ulong HashRegion(SubtitleRegion region)
    {
        var (x0, y0, w, h) = region.ToPixels(Width, Height);
        var stride = Width * 4;
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;

        var maxY = Math.Min(Height, y0 + h);
        var maxByte = Math.Min(Pixels.Length, (x0 + w) * 4);

        for (var y = y0; y < maxY; y += 4)
        {
            var row = y * stride;
            for (var x = x0 * 4; x + 2 < maxByte; x += 16)
            {
                hash ^= Pixels[row + x];
                hash *= prime;
                hash ^= Pixels[row + x + 1];
                hash *= prime;
                hash ^= Pixels[row + x + 2];
                hash *= prime;
            }
        }

        return hash;
    }
}
