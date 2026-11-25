using System.Globalization;
using System.Windows.Data;

namespace GameTranslatorIT.App.Converters;

/// <summary>
/// Confronta un valore enum con un parametro. Usato per RadioButton e DataTrigger.
/// </summary>
public sealed class EnumEqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && parameter is not null && value.Equals(parameter);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter! : Binding.DoNothing;
}
