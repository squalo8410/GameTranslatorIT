using System.Runtime.InteropServices.WindowsRuntime;
using GameTranslatorIT.Core.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WinRtBuffer = Windows.Storage.Streams.Buffer;

namespace GameTranslatorIT.Ocr;

/// <summary>
/// OCR reale basato su Windows.Media.Ocr. Nessun motore esterno o dato simulato.
/// </summary>
public sealed class WindowsOcrEngine
{
    private readonly OcrEngine _engine;

    public string LanguageTag { get; }

    private WindowsOcrEngine(OcrEngine engine, string languageTag)
    {
        _engine = engine;
        LanguageTag = languageTag;
    }

    public static bool TryCreate(string languageTag, out WindowsOcrEngine? engine)
    {
        engine = null;
        if (string.IsNullOrWhiteSpace(languageTag)) return false;

        // Alcune impostazioni salvate usano il codice breve (es. "fr") invece del tag
        // completo (es. "fr-FR"): proviamo entrambi prima di segnalare il pacchetto mancante.
        var candidates = new[] { languageTag, languageTag.Split('-')[0] };
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var winRtEngine = OcrEngine.TryCreateFromLanguage(new Language(candidate));
                if (winRtEngine is null) continue;
                engine = new WindowsOcrEngine(winRtEngine, languageTag);
                return true;
            }
            catch
            {
                // Prova il candidato successivo.
            }
        }

        return false;
    }

    public async Task<OcrTextResult> RecognizeAsync(
        CapturedFrame frame,
        SubtitleRegion region,
        int scalePercent = 200,
        int sensitivity = 70)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var (x, y, w, h) = region.ToPixels(frame.Width, frame.Height);
        var cropped = Crop(frame, x, y, w, h);
        var prepared = scalePercent > 100 ? UpscaleBgra(cropped, scalePercent / 100.0) : cropped;
        if (sensitivity > 0)
            prepared = EnhanceContrast(prepared, Math.Clamp(sensitivity, 0, 100));

        var buffer = new WinRtBuffer((uint)prepared.Pixels.Length);
        using (var stream = buffer.AsStream())
            stream.Write(prepared.Pixels, 0, prepared.Pixels.Length);

        // Con CsWinRT, AsStream() scrive i byte ma non aggiorna la Length dell'IBufferWinRT.
        // CreateCopyFromBuffer usa Length (non Capacity): senza questa riga fallisce con 0xC00D7170.
        buffer.Length = (uint)prepared.Pixels.Length;

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            prepared.Width,
            prepared.Height,
            BitmapAlphaMode.Ignore);

        var result = await _engine.RecognizeAsync(bitmap);
        var lines = result.Lines
            .Select(l => Normalize(l.Text))
            .Where(t => t.Length > 0)
            .ToList();

        return new OcrTextResult(string.Join(Environment.NewLine, lines), lines, frame.CapturedAt);
    }

    private static CapturedFrame Crop(CapturedFrame frame, int x, int y, int width, int height)
    {
        var stride = frame.Width * 4;
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            var source = ((y + row) * stride) + (x * 4);
            System.Buffer.BlockCopy(frame.Pixels, source, pixels, row * width * 4, width * 4);
        }
        return new CapturedFrame(pixels, width, height, frame.Bounds, frame.Hash, frame.CapturedAt);
    }

    private static CapturedFrame UpscaleBgra(CapturedFrame frame, double factor)
    {
        var newWidth = Math.Max(1, (int)Math.Round(frame.Width * factor));
        var newHeight = Math.Max(1, (int)Math.Round(frame.Height * factor));
        var pixels = new byte[newWidth * newHeight * 4];
        var sourceStride = frame.Width * 4;
        var targetStride = newWidth * 4;

        for (var y = 0; y < newHeight; y++)
        {
            var sourceY = Math.Min(frame.Height - 1, (int)(y / factor));
            var sourceRow = sourceY * sourceStride;
            var targetRow = y * targetStride;
            for (var x = 0; x < newWidth; x++)
            {
                var sourceX = Math.Min(frame.Width - 1, (int)(x / factor)) * 4;
                var sourceIndex = sourceRow + sourceX;
                var targetIndex = targetRow + (x * 4);
                pixels[targetIndex] = frame.Pixels[sourceIndex];
                pixels[targetIndex + 1] = frame.Pixels[sourceIndex + 1];
                pixels[targetIndex + 2] = frame.Pixels[sourceIndex + 2];
                pixels[targetIndex + 3] = 255;
            }
        }
        return new CapturedFrame(pixels, newWidth, newHeight, frame.Bounds, frame.Hash, frame.CapturedAt);
    }

    /// <summary>
    /// Aumenta il contrasto in base alla sensibilità: 0 = nessuna modifica, 100 = massima.
    /// </summary>
    private static CapturedFrame EnhanceContrast(CapturedFrame frame, int sensitivity)
    {
        var low = sensitivity * 0.5;
        var high = 255 - low;
        var range = high - low;
        var pixels = (byte[])frame.Pixels.Clone();

        for (var i = 0; i < pixels.Length; i += 4)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var value = pixels[i + channel];
                pixels[i + channel] = (byte)Math.Clamp(Math.Round((value - low) * 255.0 / range), 0, 255);
            }
        }
        return new CapturedFrame(pixels, frame.Width, frame.Height, frame.Bounds, frame.Hash, frame.CapturedAt);
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Trim();
    }
}
