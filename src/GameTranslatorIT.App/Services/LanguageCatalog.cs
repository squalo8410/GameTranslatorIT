using System.Net.Http;
using System.Text.Json;
using Windows.Media.Ocr;
using GameTranslatorIT.Translation;

namespace GameTranslatorIT.App.Services;

/// <summary>Una lingua selezionabile. <see cref="Code"/> è il tag salvato in settings.json.</summary>
public sealed record LanguageOption(
    string Code,
    string DisplayName,
    bool Installed,
    bool OnlineOnly = false,
    bool IsAuto = false);

/// <summary>Lingue segnalate dal servizio LibreTranslate locale (/languages) e dai riconoscitori OCR installati.</summary>
public sealed record AvailableLanguages(
    IReadOnlySet<string> SourceCodes,
    IReadOnlySet<string> TargetCodes,
    bool TranslationServiceReachable);

/// <summary>
/// Catalogo delle 55 lingue richieste più le 3 extra già presenti nel motore locale
/// (azero, esperanto, kirghiso). Per ogni lingua dichiariamo solo ciò che è vero:
/// modello Argos locale, riconoscitore OCR di Windows o fallback online esplicito.
/// </summary>
public static class LanguageCatalog
{
    private sealed record Definition(string Code, string ItalianName, string OcrTag, bool Local, bool Online);

    private static readonly Definition[] Languages =
    [
        new("sq", "Albanese", "sq-AL", true, true),
        new("ar", "Arabo", "ar-SA", true, true),
        new("az", "Azero", "az-Latn-AZ", true, true),
        new("eu", "Basco", "eu-ES", true, true),
        new("bn", "Bengalese", "bn-IN", true, true),
        new("bg", "Bulgaro", "bg-BG", true, true),
        new("ca", "Catalano", "ca-ES", true, true),
        new("zh-Hans", "Cinese semplificato", "zh-CN", true, true),
        new("zh-Hant", "Cinese tradizionale", "zh-TW", true, true),
        new("cs", "Ceco", "cs-CZ", true, true),
        new("da", "Danese", "da-DK", true, true),
        new("nl", "Olandese", "nl-NL", true, true),
        new("eo", "Esperanto", "eo", true, true),
        new("et", "Estone", "et-EE", true, true),
        new("fi", "Finlandese", "fi-FI", true, true),
        new("fr", "Francese", "fr-FR", true, true),
        new("gl", "Galiziano", "gl-ES", true, true),
        new("cy", "Gallese", "cy-GB", false, true),
        new("de", "Tedesco", "de-DE", true, true),
        new("el", "Greco", "el-GR", true, true),
        new("he", "Ebraico", "he-IL", true, true),
        new("hi", "Hindi", "hi-IN", true, true),
        new("hr", "Croato", "hr-HR", false, true),
        new("hu", "Ungherese", "hu-HU", true, true),
        new("id", "Indonesiano", "id-ID", true, true),
        new("ga", "Irlandese", "ga-IE", true, true),
        new("is", "Islandese", "is-IS", false, true),
        new("en", "Inglese", "en-US", true, true),
        new("it", "Italiano", "it-IT", true, true),
        new("ja", "Giapponese", "ja-JP", true, true),
        new("ko", "Coreano", "ko-KR", true, true),
        new("ky", "Kirghiso", "ky-KG", true, true),
        new("la", "Latino", "la", false, true),
        new("lv", "Lettone", "lv-LV", true, true),
        new("lt", "Lituano", "lt-LT", true, true),
        new("mk", "Macedone", "mk-MK", false, true),
        new("ms", "Malay", "ms-MY", true, true),
        new("nb", "Norvegese (Bokmål)", "nb-NO", true, true),
        new("fa", "Persiano", "fa-IR", true, true),
        new("pl", "Polacco", "pl-PL", true, true),
        new("pt", "Portoghese", "pt-PT", true, true),
        new("pt-BR", "Portoghese (Brasile)", "pt-BR", true, true),
        new("ro", "Rumeno", "ro-RO", true, true),
        new("ru", "Russo", "ru-RU", true, true),
        new("sr", "Serbo", "sr-Latn-RS", false, true),
        new("sk", "Slovacco", "sk-SK", true, true),
        new("sl", "Sloveno", "sl-SI", true, true),
        new("es", "Spagnolo", "es-ES", true, true),
        new("sw", "Swahili", "sw-KE", true, true),
        new("sv", "Svedese", "sv-SE", true, true),
        new("ta", "Tamil", "ta-IN", false, true),
        new("te", "Telugu", "te-IN", false, true),
        new("th", "Thai", "th-TH", true, true),
        new("tl", "Filippino (Tagalog)", "fil-PH", true, true),
        new("tr", "Turco", "tr-TR", true, true),
        new("uk", "Ucraino", "uk-UA", true, true),
        new("ur", "Urdu", "ur-PK", true, true),
        new("vi", "Vietnamita", "vi-VN", true, true)
    ];

    public static int TotalCount => Languages.Length;

    public static IReadOnlyList<string> GetOcrLanguageTags()
    {
        var tags = new List<string>();
        try
        {
            foreach (var language in OcrEngine.AvailableRecognizerLanguages)
            {
                if (!string.IsNullOrWhiteSpace(language.LanguageTag) && !tags.Contains(language.LanguageTag))
                    tags.Add(language.LanguageTag);
            }
        }
        catch
        {
            // Windows non espone l'elenco: il fallback è il valore già salvato.
        }

        return tags;
    }

    public static async Task<AvailableLanguages> QueryAsync(
        string baseUrl,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reachable = false;

        try
        {
            var url = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:5000" : baseUrl.TrimEnd('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 500, 8000)) };
            using var response = await http.GetAsync($"{url}/languages", cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                reachable = true;
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                foreach (var item in json.RootElement.EnumerateArray())
                {
                    var code = item.GetProperty("code").GetString();
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    sources.Add(code);

                    if (item.TryGetProperty("targets", out var values) && values.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var value in values.EnumerateArray())
                        {
                            var target = value.GetString();
                            if (!string.IsNullOrWhiteSpace(target)) targets.Add(target);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                       or JsonException)
        {
            // Servizio spento: l'elenco completo resta visibile, con gli avvisi di installazione.
        }

        return new AvailableLanguages(sources, targets, reachable);
    }

    public static IReadOnlyList<LanguageOption> BuildSourceOptions(
        IReadOnlyList<string> ocrTags,
        AvailableLanguages available,
        string currentTag,
        bool allowOnline = false)
    {
        var installedOcr = new HashSet<string>(
            ocrTags.Select(LanguageSupport.ToEngineCode), StringComparer.OrdinalIgnoreCase);
        var currentCode = LanguageSupport.ToEngineCode(currentTag);

        var options = new List<LanguageOption>
        {
            new(LanguageSupport.AutoCode, "Rilevamento automatico della lingua", true, false, true)
        };

        foreach (var definition in Languages)
        {
            var hasOcr = installedOcr.Contains(definition.Code);
            var hasOnline = definition.Online && allowOnline;
            var installed = hasOcr || hasOnline;
            var code = definition.OcrTag;

            if (string.Equals(definition.Code, currentCode, StringComparison.OrdinalIgnoreCase))
            {
                code = currentTag;
                installed = true;
            }

            var suffix = !definition.Local && definition.Online
                ? hasOnline ? " — traduzione online" : " — online da abilitare"
                : hasOcr ? string.Empty : " — OCR da installare";

            options.Add(new LanguageOption(code, $"{definition.ItalianName}{suffix}", installed, !definition.Local));
        }

        return Finalize(options, currentTag);
    }

    public static IReadOnlyList<LanguageOption> BuildTargetOptions(
        IReadOnlyList<string> ocrTags,
        AvailableLanguages available,
        string currentTag,
        bool allowOnline = false)
    {
        var currentCode = LanguageSupport.ToEngineCode(currentTag);
        var options = new List<LanguageOption>();

        foreach (var definition in Languages)
        {
            var localReady = available.TargetCodes.Contains(definition.Code);
            var onlineReady = definition.Online && allowOnline && !definition.Local;
            var installed = localReady || onlineReady;
            var code = definition.OcrTag;

            if (string.Equals(definition.Code, currentCode, StringComparison.OrdinalIgnoreCase))
            {
                code = currentTag;
                installed = true;
            }

            var suffix = !definition.Local
                ? onlineReady ? " — online" : " — online da abilitare"
                : localReady ? string.Empty : " — modello da installare";

            options.Add(new LanguageOption(code, $"{definition.ItalianName}{suffix}", installed, !definition.Local));
        }

        return Finalize(options, currentTag);
    }

    public static IReadOnlyList<LanguageOption> BuildOcrOptions(IReadOnlyList<string> ocrTags, string currentTag)
    {
        var options = ocrTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(tag => new LanguageOption(tag, DisplayNameForTag(tag), true))
            .ToList();

        if (!string.IsNullOrWhiteSpace(currentTag) &&
            !options.Any(o => string.Equals(o.Code, currentTag, StringComparison.OrdinalIgnoreCase)))
            options.Insert(0, new LanguageOption(currentTag, $"{currentTag} (da installare)", false));

        return options.OrderBy(o => o.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string DisplayNameForTag(string tag)
    {
        var code = LanguageSupport.ToEngineCode(tag);
        var definition = Languages.FirstOrDefault(l =>
            string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        return definition is null ? tag : $"{definition.ItalianName} ({tag})";
    }

    public static bool IsRightToLeft(string tag) => LanguageSupport.IsRightToLeft(tag);

    private static IReadOnlyList<LanguageOption> Finalize(List<LanguageOption> options, string currentTag)
    {
        if (!options.Any(o => string.Equals(o.Code, currentTag, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(currentTag))
            options.Add(new LanguageOption(currentTag, $"{currentTag} (impostazione attuale)", true));

        return options.OrderBy(o => o.IsAuto ? 0 : 1)
            .ThenBy(o => o.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
