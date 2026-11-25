using System.Windows;
using System.Windows.Controls;
using GameTranslatorIT.App.Services;

namespace GameTranslatorIT.App.Converters;

/// <summary>
/// Mostra solo il codice BCP-47 (es. en-US) nel campo chiuso delle combo lingua,
/// lasciando il template predefinito per tutte le altre combo.
/// </summary>
public sealed class LanguageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? LanguageTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
        => item is LanguageOption && LanguageTemplate is not null
            ? LanguageTemplate
            : base.SelectTemplate(item, container);
}
