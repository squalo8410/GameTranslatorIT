using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Translation;

/// <summary>
/// Provider di traduzione locale. Parla con un'istanza LibreTranslate in ascolto su loopback,
/// basata su Argos Translate. Nessuna richiesta esce dal PC.
/// API verificata: POST /translate con q, source, target -> { "translatedText": "..." }.
/// </summary>
public sealed class LibreTranslateProvider : ITranslationProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly int _timeoutMs;
    private bool _disposed;

    public string DisplayName => "LibreTranslate locale (Argos)";

    public LibreTranslateProvider(string baseUrl, int timeoutMs)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:5000" : baseUrl.TrimEnd('/');
        _timeoutMs = timeoutMs is >= 500 and <= 30000 ? timeoutMs : 4000;
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(_timeoutMs) };
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var sw = Stopwatch.StartNew();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["q"] = text,
            ["source"] = LanguageSupport.ToEngineCode(context.SourceLanguage),
            ["target"] = LanguageSupport.ToEngineCode(context.TargetLanguage)
        });

        using var response = await _http.PostAsync($"{_baseUrl}/translate", form, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var translated = json.RootElement.GetProperty("translatedText").GetString() ?? string.Empty;

        sw.Stop();
        return new TranslationResult(text, translated, DisplayName, sw.Elapsed);
    }

    /// <summary>
    /// Verifica che il servizio locale sia raggiungibile. Usata dal pulsante "Verifica" nelle impostazioni.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync($"{_baseUrl}/languages", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }
}
