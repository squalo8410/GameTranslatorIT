namespace GameTranslatorIT.Translation;

/// <summary>
/// Mappa i tag BCP-47 delle impostazioni ai codici usati dai motori di traduzione.
/// L'elenco "locale" contiene solo le lingue per cui Argos/LibreTranslate ha davvero
/// un modello installato; le altre sono dichiarate disponibili solo se il fallback
/// online è stato esplicitamente abilitato dall'utente.
/// </summary>
public static class LanguageSupport
{
    public const string AutoCode = "auto";

    /// <summary>50 lingue con modelli Argos installati dal servizio locale.</summary>
    public static readonly IReadOnlySet<string> LocalCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "az", "bg", "bn", "ca", "cs", "da", "de", "el", "en", "eo", "es", "et", "eu",
        "fa", "fi", "fr", "ga", "gl", "he", "hi", "hu", "id", "it", "ja", "ko", "ky", "lt",
        "lv", "ms", "nb", "nl", "pl", "pt", "pt-BR", "ro", "ru", "sk", "sl", "sq", "sv",
        "sw", "th", "tl", "tr", "uk", "ur", "vi", "zh-Hans", "zh-Hant"
    };

    /// <summary>Lingue richieste dall'utente senza alcun modello Argos nell'indice ufficiale.</summary>
    public static readonly IReadOnlySet<string> OnlineOnlyCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cy", "hr", "is", "la", "mk", "sr", "ta", "te"
    };

    /// <summary>Lingue traducibili dal fallback online (verificate una per una).</summary>
    public static readonly IReadOnlySet<string> OnlineCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AutoCode, "ar", "az", "bg", "bn", "ca", "cs", "cy", "da", "de", "el", "en", "eo", "es",
        "et", "eu", "fa", "fi", "fr", "ga", "gl", "he", "hi", "hr", "hu", "id", "is", "it",
        "ja", "ko", "ky", "la", "lt", "lv", "mk", "ms", "nb", "nl", "pl", "pt", "pt-BR", "ro",
        "ru", "sk", "sl", "sq", "sr", "sv", "sw", "ta", "te", "th", "tl", "tr", "uk", "ur",
        "vi", "zh-Hans", "zh-Hant"
    };

    /// <summary>Converte un tag salvato in settings.json nel codice atteso dal motore.</summary>
    public static string ToEngineCode(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var value = tag.Trim();
        if (value.Equals(AutoCode, StringComparison.OrdinalIgnoreCase)) return AutoCode;

        var parts = value.Split('-');
        var code = parts[0].ToLowerInvariant();
        if (code == "fil") return "tl";
        if (code == "no") return "nb";
        if (parts.Length < 2) return code;

        var suffix = parts[1];
        if (code == "pt" && suffix.Equals("BR", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        if (code == "zh" && suffix.Equals("CN", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        if (code == "zh" && suffix.Equals("TW", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
        if (code == "zh" && suffix.Equals("Hans", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        if (code == "zh" && suffix.Equals("Hant", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
        return code;
    }

    /// <summary>True per le lingue che usano alfabeti da destra a sinistra.</summary>
    public static bool IsRightToLeft(string? codeOrTag)
    {
        var code = ToEngineCode(codeOrTag);
        return code is "ar" or "he" or "fa" or "ur";
    }

    /// <summary>True se il testo contiene caratteri arabi, ebraici o persiani/urdu.</summary>
    public static bool ContainsRightToLeftText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var c in text)
        {
            if (c is >= '\u0590' and <= '\u08FF' || c is >= '\uFB1D' and <= '\uFEFC')
                return true;
        }
        return false;
    }
}
