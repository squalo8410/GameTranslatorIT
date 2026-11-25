using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Translation;

/// <summary>
/// Fallback esplicito per le lingue senza modello Argos (islandese, croato, serbo,
/// macedone, tamil, telugu, gallese, latino). Non viene mai usato automaticamente:
/// l'app lo chiama solo quando l'utente abilita l'opzione nelle impostazioni e la
/// coppia richiesta non è coperta dal servizio locale.
/// </summary>
public sealed class OnlineTranslationProvider : ITranslationProvider, IDisposable
{
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single";
    private readonly HttpClient _http;
    private bool _disposed;

    public string DisplayName => "Online (solo lingue senza modello locale)";

    public OnlineTranslationProvider(int timeoutMs = 8000)
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 3000, 30000))
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GameTranslatorIT/1.0 (+fallback lingue locali assenti)");
    }

    public static bool IsSupported(string? codeOrTag)
        => LanguageSupport.OnlineCodes.Contains(LanguageSupport.ToEngineCode(codeOrTag));

    public async Task<TranslationResult> TranslateAsync(
        string text,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var source = LanguageSupport.ToEngineCode(context.SourceLanguage);
        var target = LanguageSupport.ToEngineCode(context.TargetLanguage);
        if (!IsSupported(source) || !IsSupported(target))
            throw new InvalidOperationException($"Lingua online non supportata: {source} → {target}.");

        var query = string.Join('&',
            "client=gtx",
            $"sl={Uri.EscapeDataString(source)}",
            $"tl={Uri.EscapeDataString(target)}",
            "dt=t",
            $"q={Uri.EscapeDataString(text)}");

        var sw = Stopwatch.StartNew();
        using var response = await _http.GetAsync($"{Endpoint}?{query}", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (json.RootElement.ValueKind != JsonValueKind.Array ||
            json.RootElement.GetArrayLength() == 0 ||
            json.RootElement[0].ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Risposta di traduzione online non valida.");

        var translated = string.Concat(json.RootElement[0].EnumerateArray()
            .Where(segment => segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0)
            .Select(segment => segment[0].GetString() ?? string.Empty));

        if (string.IsNullOrWhiteSpace(translated))
            throw new InvalidOperationException("La traduzione online non ha restituito testo.");

        sw.Stop();
        return new TranslationResult(text, translated, DisplayName, sw.Elapsed);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }
}
