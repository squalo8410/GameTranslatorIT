using Windows.Globalization;
using Windows.Media.Ocr;

namespace GameTranslatorIT.Ocr;

/// <summary>
/// Verifica reale della disponibilità dei pacchetti lingua OCR di Windows.
/// </summary>
public static class OcrLanguageChecker
{
    public static bool IsLanguageAvailable(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return false;
        try
        {
            return OcrEngine.IsLanguageSupported(new Language(languageTag));
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<string> AvailableLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(l => l.LanguageTag)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
