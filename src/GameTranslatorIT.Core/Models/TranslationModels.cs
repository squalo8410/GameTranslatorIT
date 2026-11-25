namespace GameTranslatorIT.Core.Models;

public sealed record TranslationContext(
    string SourceLanguage,
    string TargetLanguage,
    IReadOnlyList<string> PreviousSourceLines,
    IReadOnlyList<string> PreviousTranslatedLines,
    string? GlossaryText);

public sealed record TranslationResult(
    string SourceText,
    string TranslatedText,
    string ProviderName,
    TimeSpan Elapsed);

public interface ITranslationProvider
{
    string DisplayName { get; }
    Task<TranslationResult> TranslateAsync(
        string text,
        TranslationContext context,
        CancellationToken cancellationToken);
}
