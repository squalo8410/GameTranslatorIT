using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.App.Converters;

/// <summary>
/// Converte uno ComponentState nel colore del relativo indicatore di stato.
/// </summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var state = value is ComponentState s ? s : ComponentState.NonInizializzato;
        var color = state switch
        {
            ComponentState.Attivo => Colors.MediumSeaGreen,
            ComponentState.Pronto => Colors.SteelBlue,
            ComponentState.Attesa => Colors.Khaki,
            ComponentState.Avviso => Colors.Orange,
            ComponentState.Errore => Colors.IndianRed,
            ComponentState.Disabilitato => Colors.DimGray,
            _ => Colors.Gray
        };

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
