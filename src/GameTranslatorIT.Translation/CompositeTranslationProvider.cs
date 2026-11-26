using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Translation;

/// <summary>
/// Usa LibreTranslate locale quando la coppia è coperta dai modelli installati.
/// Solo le 8 lingue senza modello Argos possono usare il fallback online, e solo
/// se l'utente lo ha abilitato esplicitamente.
/// </summary>
public sealed class CompositeTranslationProvider : ITranslationProvider
{
    private readonly LibreTranslateProvider _local;
    private readonly OnlineTranslationProvider _online;
    private readonly Func<bool> _allowOnline;
    private IReadOnlySet<string> _localSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<string> _localTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string DisplayName => "LibreTranslate locale + online (solo se abilitato)";

    public CompositeTranslationProvider(
        LibreTranslateProvider local,
        OnlineTranslationProvider online,
        Func<bool> allowOnline)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _online = online ?? throw new ArgumentNullException(nameof(online));
        _allowOnline = allowOnline ?? throw new ArgumentNullException(nameof(allowOnline));
    }

    /// <summary>Aggiorna sorgenti e destinazioni reali restituite da /languages.</summary>
    public void UpdateLanguages(IEnumerable<string> sources, IEnumerable<string> targets)
    {
        _localSources = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
        _localTargets = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Compatibilità con i chiamanti che aggiornano solo le destinazioni.</summary>
    public void UpdateLocalTargets(IEnumerable<string> targets)
        => _localTargets = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);

    public async Task<TranslationResult> TranslateAsync(
        string text,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var source = LanguageSupport.ToEngineCode(context.SourceLanguage);
        var target = LanguageSupport.ToEngineCode(context.TargetLanguage);

        var knownSources = _localSources.Count > 0 ? _localSources : LanguageSupport.LocalCodes;
        var knownTargets = _localTargets.Count > 0 ? _localTargets : LanguageSupport.LocalCodes;
        var localSourceOk = source == LanguageSupport.AutoCode || knownSources.Contains(source);
        var localTargetOk = knownTargets.Contains(target);

        if (localSourceOk && localTargetOk)
            return await _local.TranslateAsync(text, context, cancellationToken).ConfigureAwait(false);

        // La rete è ammessa solo quando serve davvero per una delle 8 lingue
        // senza modello: mai come scorciatoia per coppie già coperte in locale.
        var onlinePair =
            LanguageSupport.OnlineCodes.Contains(source) &&
            LanguageSupport.OnlineCodes.Contains(target) &&
            (LanguageSupport.OnlineOnlyCodes.Contains(source) ||
             LanguageSupport.OnlineOnlyCodes.Contains(target));

        if (_allowOnline() && onlinePair)
            return await _online.TranslateAsync(text, context, cancellationToken).ConfigureAwait(false);

        throw new InvalidOperationException(
            $"Nessun motore disponibile per {source} → {target}. " +
            "Il modello locale non è installato e la traduzione online non è abilitata: " +
            "apri Impostazioni → Lingue e traduzione e attiva l'opzione online.");
    }
}
