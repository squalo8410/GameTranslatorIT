using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Translation;

/// <summary>
/// Usa sempre LibreTranslate locale quando la coppia è coperta dai modelli installati.
/// Per le 8 lingue senza modello (e solo se l'utente lo abilita) passa al fallback
/// online, segnalando il provider effettivo nel risultato e nell'overlay.
/// </summary>
public sealed class CompositeTranslationProvider : ITranslationProvider
{
    private readonly LibreTranslateProvider _local;
    private readonly OnlineTranslationProvider _online;
    private readonly Func<bool> _allowOnline;
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

    /// <summary>Aggiorna l'elenco reale restituito da /languages dopo ogni verifica.</summary>
    public void UpdateLocalTargets(IEnumerable<string> targets)
        => _localTargets = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);

    public async Task<TranslationResult> TranslateAsync(
        string text,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var source = LanguageSupport.ToEngineCode(context.SourceLanguage);
        var target = LanguageSupport.ToEngineCode(context.TargetLanguage);

        var knownTargets = _localTargets.Count > 0 ? _localTargets : LanguageSupport.LocalCodes;
        var localSourceOk = source == LanguageSupport.AutoCode || knownTargets.Contains(source);
        var localTargetOk = knownTargets.Contains(target);

        if (localSourceOk && localTargetOk)
        {
            var result = await _local.TranslateAsync(text, context, cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (_allowOnline() &&
            LanguageSupport.OnlineCodes.Contains(source) &&
            LanguageSupport.OnlineCodes.Contains(target))
        {
            var result = await _online.TranslateAsync(text, context, cancellationToken).ConfigureAwait(false);
            return result;
        }

        throw new InvalidOperationException(
            $"Nessun motore disponibile per {source} → {target}. " +
            "Il modello locale non è installato e la traduzione online non è abilitata: " +
            "apri Impostazioni → Lingue e traduzione e attiva l'opzione online.");
    }
}
