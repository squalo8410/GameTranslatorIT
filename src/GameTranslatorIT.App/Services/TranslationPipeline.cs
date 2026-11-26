using GameTranslatorIT.Capture;
using GameTranslatorIT.Core.Models;
using GameTranslatorIT.Ocr;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Pipeline in background: cattura → rilevamento modifiche → OCR → deduplica → traduzione "latest-wins".
/// Con i riquadri attivi ogni area ha uno stato separato: una missione ferma non può
/// quindi influenzare la deduplica dei sottotitoli che cambiano ogni pochi secondi.
/// </summary>
public sealed class TranslationPipeline : IDisposable
{
    private sealed class RegionState
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public SubtitleRegion Region { get; set; } = SubtitleRegion.Auto;
        public bool Muted { get; set; }
        public RegionMuteMode MuteMode { get; set; }
        public ulong LastHash { get; set; }
        public string LastText { get; set; } = string.Empty;
        public long Generation { get; set; }
        public HashSet<string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Queue<string> SeenOrder { get; } = new();
        public List<string> PreviousSources { get; } = new();
        public List<string> PreviousTranslations { get; } = new();
    }

    private readonly AppSettings _settings;
    private volatile ITranslationProvider _provider;
    private readonly GlossaryService _glossary = new();
    private readonly Dictionary<string, RegionState> _regionStates = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private GdiScreenCapture? _capture;
    private volatile WindowsOcrEngine? _ocr;
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private IReadOnlyList<WindowInfo> _windows = Array.Empty<WindowInfo>();
    private string _ocrTagUsed = string.Empty;
    private bool _disposed;

    public event EventHandler<SubtitleEventArgs>? SubtitleRecognized;
    public event EventHandler<TranslationEventArgs>? Translated;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<ComponentStatus>? CaptureStatusChanged;
    public event EventHandler<ComponentStatus>? OcrStatusChanged;
    public event EventHandler<ComponentStatus>? TranslationStatusChanged;

    public bool IsRunning => _cts is not null;

    public TranslationPipeline(AppSettings settings, ITranslationProvider provider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void Start(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<WindowInfo> windows)
    {
        if (IsRunning) return;

        var requestedOcrTag = EffectiveOcrLanguage;
        if (!TryCreateOcr(requestedOcrTag, out var ocr, out var usedOcrTag) || ocr is null)
        {
            OcrStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Errore,
                $"Nessun pacchetto OCR di Windows disponibile per '{requestedOcrTag}'"));
            ErrorOccurred?.Invoke(this,
                $"OCR non disponibile per '{requestedOcrTag}'. Installa un pacchetto lingua di Windows.");
            return;
        }
        _ocrTagUsed = usedOcrTag;

        _monitors = monitors.ToArray();
        _windows = windows.ToArray();
        var area = ResolveArea();
        if (area.Width <= 0 || area.Height <= 0)
        {
            CaptureStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Errore, "Area di cattura non valida"));
            ErrorOccurred?.Invoke(this, "Impossibile determinare l'area di cattura. Aggiorna monitor e finestre.");
            return;
        }

        _glossary.Load(_settings.GlossaryPath);
        _ocr = ocr;
        _capture = new GdiScreenCapture();
        _regionStates.Clear();

        _cts = new CancellationTokenSource();
        CaptureStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Attivo,
            $"Acquisizione reale attiva — {area.Width}x{area.Height}"));
        OcrStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Pronto,
            string.Equals(usedOcrTag, requestedOcrTag, StringComparison.OrdinalIgnoreCase)
                ? $"Windows OCR — {usedOcrTag} (voce {ocr.LanguageTag})"
                : $"Windows OCR — {usedOcrTag} invece di {requestedOcrTag} (pacchetto non installato)"));
        TranslationStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Pronto,
            $"{_provider.DisplayName} — glossario: {_glossary.Count} voci"));

        _loop = Task.Run(() => RunLoopAsync(_cts!.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();

        _capture?.Dispose();
        _capture = null;
        _ocr = null;
        _loop = null;
        _regionStates.Clear();

        CaptureStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Pronto, "Acquisizione interrotta"));
        OcrStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Disabilitato, "OCR interrotto"));
        TranslationStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Disabilitato, "Traduzione interrotta"));
    }

    /// <summary>
    /// Lingua del riconoscitore OCR. Con "auto" la traduzione indovina dal testo,
    /// ma Windows deve comunque sapere quale alfabeto leggere dallo schermo.
    /// </summary>
    private string EffectiveOcrLanguage
    {
        get
        {
            if (!string.Equals(_settings.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
                return _settings.SourceLanguage;
            return string.IsNullOrWhiteSpace(_settings.OcrLanguage) ? "en-US" : _settings.OcrLanguage;
        }
    }

    /// <summary>Sostituisce il provider senza interrompere la cattura in corso.</summary>
    public void UpdateProvider(ITranslationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        TranslationStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Pronto,
            $"{_provider.DisplayName} — glossario: {_glossary.Count} voci"));
    }

    /// <summary>
    /// Cambia lingua OCR e azzera la deduplica in tempo reale, senza fermare il loop
    /// di cattura. La lingua di traduzione è letta dalle impostazioni a ogni frase.
    /// </summary>
    public bool ApplyLanguageSettings()
    {
        if (!IsRunning) return true;

        var requestedTag = EffectiveOcrLanguage;
        if (!TryCreateOcr(requestedTag, out var ocr, out var usedTag) || ocr is null)
        {
            OcrStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Errore,
                $"Nessun pacchetto OCR di Windows disponibile per '{requestedTag}'"));
            ErrorOccurred?.Invoke(this, $"OCR non disponibile per '{requestedTag}'. Installa il pacchetto lingua di Windows.");
            return false;
        }
        _ocrTagUsed = usedTag;

        _ocr = ocr;
        foreach (var state in _regionStates.Values)
        {
            state.LastHash = 0;
            state.LastText = string.Empty;
            state.Seen.Clear();
            state.SeenOrder.Clear();
            state.PreviousSources.Clear();
            state.PreviousTranslations.Clear();
        }

        OcrStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Pronto,
            $"Windows OCR — {usedTag} (voce {ocr.LanguageTag}) — lingua cambiata senza riavvio"));
        return true;
    }

    /// <summary>Aggiorna monitor e finestre senza riavviare la cattura: la finestra del gioco può essere spostata o chiusa.</summary>
    public void UpdateTargets(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<WindowInfo> windows)
    {
        _monitors = monitors.ToArray();
        _windows = windows.ToArray();
    }

    /// <summary>Area di cattura ricalcolata a ogni frame dall'elenco aggiornato.</summary>
    public ScreenRect ResolveArea() => GameWindowResolver.ResolveArea(
        _settings.CaptureMode,
        _settings.SelectedMonitorIndex,
        _settings.SelectedWindowHandle,
        _settings.SelectedWindowPid,
        _settings.SelectedWindowTitle,
        _monitors,
        _windows);

    private static bool TryCreateOcr(string requestedTag, out WindowsOcrEngine? ocr, out string usedTag)
    {
        var candidates = new List<string> { requestedTag };
        candidates.AddRange(LanguageCatalog.GetOcrLanguageTags());
        candidates.AddRange(new[] { "en-US", "it-IT" });

        foreach (var candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (WindowsOcrEngine.TryCreate(candidate, out var engine) && engine is not null)
            {
                ocr = engine;
                usedTag = candidate;
                return true;
            }
        }

        ocr = null;
        usedTag = requestedTag;
        return false;
    }

    private IEnumerable<CaptureRegionConfig> ResolveActiveRegions()
    {
        if (!_settings.UseCaptureRegions)
        {
            yield return new CaptureRegionConfig
            {
                Id = "__area_unica__",
                Name = "Sottotitoli",
                Left = _settings.UseManualSubtitleRegion ? _settings.ManualRegionLeft : SubtitleRegion.Auto.Left,
                Top = _settings.UseManualSubtitleRegion ? _settings.ManualRegionTop : SubtitleRegion.Auto.Top,
                Width = _settings.UseManualSubtitleRegion ? _settings.ManualRegionWidth : SubtitleRegion.Auto.Width,
                Height = _settings.UseManualSubtitleRegion ? _settings.ManualRegionHeight : SubtitleRegion.Auto.Height,
                Enabled = true,
                Muted = false,
                MuteMode = RegionMuteMode.SoloVoce
            };
            yield break;
        }

        foreach (var region in _settings.CaptureRegions.ToArray())
        {
            if (region.Enabled) yield return region;
        }
    }

    private RegionState GetState(CaptureRegionConfig config)
    {
        var region = config.ToSubtitleRegion();
        if (!_regionStates.TryGetValue(config.Id, out var state))
        {
            state = new RegionState { Id = config.Id };
            _regionStates[config.Id] = state;
        }

        state.Name = string.IsNullOrWhiteSpace(config.Name) ? "Riquadro" : config.Name;
        state.Muted = config.Muted;
        state.MuteMode = config.MuteMode;

        if (state.Region != region)
        {
            state.Region = region;
            state.LastHash = 0;
            state.LastText = string.Empty;
            state.Seen.Clear();
            state.SeenOrder.Clear();
        }

        return state;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var area = ResolveArea();
                if (area.Width <= 0 || area.Height <= 0)
                {
                    await Task.Delay(Math.Clamp(_settings.UpdateIntervalMs, 100, 2000), ct);
                    continue;
                }

                var frame = _capture!.CaptureRegion(area);

                foreach (var config in ResolveActiveRegions())
                {
                    ct.ThrowIfCancellationRequested();
                    var state = GetState(config);

                    // Blocca: nessuna lettura e nessun aggiornamento finché il muto è attivo.
                    if (state.Muted && state.MuteMode == RegionMuteMode.Blocca)
                    {
                        state.LastHash = 0;
                        continue;
                    }

                    var hash = frame.HashRegion(state.Region);
                    if (hash == state.LastHash) continue;
                    state.LastHash = hash;

                    var ocrResult = await _ocr!.RecognizeAsync(
                        frame, state.Region, _settings.OcrUpscalePercent, _settings.OcrSensitivity);
                    var text = ocrResult.Text.Trim();

                    if (text.Length == 0) continue;
                    if (string.Equals(text, state.LastText, StringComparison.Ordinal)) continue;
                    if (state.Seen.Contains(text)) continue;

                    state.LastText = text;
                    Remember(state, text);

                    SubtitleRecognized?.Invoke(this,
                        new SubtitleEventArgs(text, ocrResult.DetectedAt, state.Id, state.Name));
                    OcrStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Attivo,
                        $"{state.Name}: {Shorten(text, 60)}"));

                    var generation = ++state.Generation;
                    _ = TranslateAndPublishAsync(text, generation, state, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Errore pipeline: {Describe(ex)}");
            }

            try
            {
                await Task.Delay(Math.Clamp(_settings.UpdateIntervalMs, 100, 2000), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TranslateAndPublishAsync(string text, long generation, RegionState state, CancellationToken ct)
    {
        TranslationStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Attesa,
            $"{state.Name}: traduzione in corso…"));
        try
        {
            var context = new TranslationContext(
                _settings.SourceLanguage,
                _settings.TargetLanguage,
                state.PreviousSources.ToArray(),
                state.PreviousTranslations.ToArray(),
                _glossary.ContextText);

            var result = await _provider.TranslateAsync(text, context, ct);
            if (generation != state.Generation || ct.IsCancellationRequested) return;

            var translated = _glossary.Apply(result.TranslatedText);

            state.PreviousSources.Add(text);
            if (state.PreviousSources.Count > 3) state.PreviousSources.RemoveAt(0);
            state.PreviousTranslations.Add(translated);
            if (state.PreviousTranslations.Count > 3) state.PreviousTranslations.RemoveAt(0);

            TranslationStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Attivo,
                $"{state.Name}: tradotto in {result.Elapsed.TotalMilliseconds:0} ms"));
            Translated?.Invoke(this, new TranslationEventArgs(text, translated, state.Id, state.Name));
        }
        catch (OperationCanceledException)
        {
            // Interruzione dell'utente: nessun evento da pubblicare.
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            var details = Describe(ex);
            TranslationStatusChanged?.Invoke(this, new ComponentStatus(ComponentState.Errore,
                $"{state.Name}: {details}"));
            ErrorOccurred?.Invoke(this, $"Traduzione di '{state.Name}' non riuscita: {details}");
        }
    }

    private static void Remember(RegionState state, string text)
    {
        if (state.Seen.Contains(text)) return;
        state.Seen.Add(text);
        state.SeenOrder.Enqueue(text);
        while (state.SeenOrder.Count > 200)
        {
            var old = state.SeenOrder.Dequeue();
            state.Seen.Remove(old);
        }
    }

    /// <summary>
    /// Alcune eccezioni COM/WinRT hanno Message vuoto: tipo e HResult rendono il log diagnosticabile.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var message = string.IsNullOrWhiteSpace(ex.Message) ? "(messaggio vuoto)" : ex.Message;
        var hresult = ex.HResult == 0 ? string.Empty : $" [0x{ex.HResult:X8}]";
        return $"{ex.GetType().Name}: {message}{hresult}";
    }

    private static string Shorten(string text, int max)
        => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
