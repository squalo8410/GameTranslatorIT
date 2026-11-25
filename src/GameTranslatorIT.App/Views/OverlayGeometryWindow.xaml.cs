using System.Globalization;
using System.Windows;
using GameTranslatorIT.App.ViewModels;

namespace GameTranslatorIT.App.Views;

/// <summary>Finestra per impostare posizione e dimensioni dell'overlay in pixel.</summary>
public sealed partial class OverlayGeometryWindow : Window
{
    private const double DefaultLeft = 996;
    private const double DefaultTop = 24;
    private const double DefaultWidth = 900;
    private const double DefaultHeight = 180;

    private readonly MainViewModel _viewModel;

    public OverlayGeometryWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel.Settings;

        var settings = viewModel.Settings;
        LeftBox.Text = Number(settings.OverlayLeft);
        TopBox.Text = Number(settings.OverlayTop);
        WidthBox.Text = Number(settings.OverlayWidth);
        HeightBox.Text = Number(settings.OverlayHeight);
    }

    private static string Number(double value) => value.ToString("0", CultureInfo.InvariantCulture);

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryNumber(LeftBox.Text, out var left) ||
            !TryNumber(TopBox.Text, out var top) ||
            !TryNumber(WidthBox.Text, out var width) ||
            !TryNumber(HeightBox.Text, out var height))
        {
            MessageBox.Show(this, "Inserisci quattro numeri validi: X, Y, larghezza e altezza.",
                "Overlay", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _viewModel.SetOverlayGeometry(left, top, width, height);
        DialogResult = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetOverlayGeometry(DefaultLeft, DefaultTop, DefaultWidth, DefaultHeight);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static bool TryNumber(string? text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
