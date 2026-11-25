using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameTranslatorIT.App.Services;
using GameTranslatorIT.Capture;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.App.Views;

/// <summary>
/// Finestra di selezione a schermo intero: mostra un'istantanea reale dell'area di cattura,
/// converte il riquadro disegnato in coordinate relative a quell'area e permette di
/// spostare e ridimensionare il riquadro selezionato con le maniglie agli angoli
/// oppure digitando larghezza e altezza in pixel.
/// </summary>
public sealed partial class RegionSelectorWindow : Window
{
    private readonly ScreenRect _area;
    private readonly ObservableCollection<CaptureRegionConfig> _regions;
    private readonly GdiScreenCapture _capture = new();
    private readonly List<Border> _outlines = new();
    private readonly double _imageLeft;
    private readonly double _imageTop;
    private int _selectedIndex;
    private bool _dragging;
    private Point _start;

    private bool _interacting;
    private string _interactionKind = string.Empty;
    private Point _interactionStart;
    private SubtitleRegion _interactionRegion = new(0, 0, 0, 0);
    private SubtitleRegion _renderedRegion = new(0, 0, 0, 0);

    public RegionSelectorWindow(ScreenRect area, ObservableCollection<CaptureRegionConfig> regions, int selectedIndex)
    {
        InitializeComponent();
        _area = area;
        _regions = regions;
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, regions.Count - 1));

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        Left = virtualLeft;
        Top = virtualTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _imageLeft = area.Left - virtualLeft;
        _imageTop = area.Top - virtualTop;
        Canvas.SetLeft(Preview, _imageLeft);
        Canvas.SetTop(Preview, _imageTop);
        Preview.Width = area.Width;
        Preview.Height = area.Height;

        var frame = _capture.CaptureRegion(area);
        Preview.Source = BitmapSource.Create(
            frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null,
            frame.Pixels, frame.Width * 4);

        RegionCombo.ItemsSource = regions;
        RegionCombo.SelectedIndex = _selectedIndex;
        DrawOutlines();

        SourceInitialized += (_, _) => WindowCaptureExclusion.Exclude(this);
        Closed += (_, _) =>
        {
            WindowCaptureExclusion.Restore(this);
            _capture.Dispose();
        };
    }

    private SubtitleRegion? CurrentRegion
    {
        get
        {
            if (_selectedIndex < 0 || _selectedIndex >= _regions.Count) return null;
            var config = _regions[_selectedIndex];
            return config.Width <= 0 || config.Height <= 0 ? null : config.ToSubtitleRegion();
        }
    }

    private void DrawOutlines()
    {
        foreach (var outline in _outlines) SelectionCanvas.Children.Remove(outline);
        _outlines.Clear();

        for (var i = 0; i < _regions.Count; i++)
        {
            if (i == _selectedIndex) continue;
            var config = _regions[i];
            if (config.Width <= 0 || config.Height <= 0) continue;

            var outline = new Border
            {
                IsHitTestVisible = false,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 120, 180, 255)),
                Background = Brushes.Transparent
            };

            Canvas.SetLeft(outline, _imageLeft + config.Left * _area.Width);
            Canvas.SetTop(outline, _imageTop + config.Top * _area.Height);
            outline.Width = config.Width * _area.Width;
            outline.Height = config.Height * _area.Height;
            SelectionCanvas.Children.Add(outline);
            _outlines.Add(outline);
        }

        UpdateSelectionFrame();
    }

    private void UpdateSelectionFrame()
    {
        var region = CurrentRegion;
        if (region is null)
        {
            SelectionFrame.Visibility = Visibility.Collapsed;
            SizeLabel.Text = "Nessun riquadro selezionato";
            LeftBox.Text = string.Empty;
            TopBox.Text = string.Empty;
            WidthBox.Text = string.Empty;
            HeightBox.Text = string.Empty;
            return;
        }

        RenderFrame(region);
        LeftBox.Text = Math.Round(region.Left * _area.Width).ToString("0", CultureInfo.InvariantCulture);
        TopBox.Text = Math.Round(region.Top * _area.Height).ToString("0", CultureInfo.InvariantCulture);
        WidthBox.Text = Math.Round(region.Width * _area.Width).ToString("0", CultureInfo.InvariantCulture);
        HeightBox.Text = Math.Round(region.Height * _area.Height).ToString("0", CultureInfo.InvariantCulture);
    }

    private void RenderFrame(SubtitleRegion region)
    {
        _renderedRegion = region;
        SelectionFrame.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionFrame, _imageLeft + region.Left * _area.Width);
        Canvas.SetTop(SelectionFrame, _imageTop + region.Top * _area.Height);
        SelectionFrame.Width = region.Width * _area.Width;
        SelectionFrame.Height = region.Height * _area.Height;

        SizeLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0} × {1} px  ({2:0}% × {3:0}%)",
            Math.Round(region.Width * _area.Width),
            Math.Round(region.Height * _area.Height),
            region.Width * 100,
            region.Height * 100);
    }

    private void CommitRegion(SubtitleRegion region)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _regions.Count) return;
        var config = _regions[_selectedIndex];
        config.Set(region);
        config.Enabled = true;
        DrawOutlines();
    }

    private void RegionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedIndex = RegionCombo.SelectedIndex;
        DrawOutlines();
    }

    private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || CurrentRegion is null) return;
        StartInteraction("move", e);
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || CurrentRegion is null) return;
        var direction = (sender as FrameworkElement)?.Tag?.ToString();
        if (string.IsNullOrEmpty(direction)) return;
        StartInteraction(direction, e);
    }

    private void StartInteraction(string kind, MouseButtonEventArgs e)
    {
        var region = CurrentRegion;
        if (region is null) return;
        _interacting = true;
        _interactionKind = kind;
        _interactionStart = e.GetPosition(SelectionCanvas);
        _interactionRegion = region;
        RenderFrame(region);
        SelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _interacting) return;
        _dragging = true;
        _start = e.GetPosition(SelectionCanvas);
        Canvas.SetLeft(SelectionRect, _start.X);
        Canvas.SetTop(SelectionRect, _start.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;
        SelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_interacting)
        {
            RenderFrame(ComputeInteractionRegion(e.GetPosition(SelectionCanvas)));
            return;
        }

        if (!_dragging) return;
        var current = e.GetPosition(SelectionCanvas);
        Canvas.SetLeft(SelectionRect, Math.Min(_start.X, current.X));
        Canvas.SetTop(SelectionRect, Math.Min(_start.Y, current.Y));
        SelectionRect.Width = Math.Abs(current.X - _start.X);
        SelectionRect.Height = Math.Abs(current.Y - _start.Y);
    }

    private SubtitleRegion ComputeInteractionRegion(Point current)
    {
        var start = _interactionRegion;
        var dx = (current.X - _interactionStart.X) / _area.Width;
        var dy = (current.Y - _interactionStart.Y) / _area.Height;

        if (_interactionKind == "move")
        {
            return new SubtitleRegion(
                Math.Clamp(start.Left + dx, 0, 1 - start.Width),
                Math.Clamp(start.Top + dy, 0, 1 - start.Height),
                start.Width,
                start.Height);
        }

        const double minPixels = 20;
        var minWidth = minPixels / _area.Width;
        var minHeight = minPixels / _area.Height;
        var left = start.Left;
        var top = start.Top;
        var right = start.Left + start.Width;
        var bottom = start.Top + start.Height;

        if (_interactionKind.Contains("W")) left = Math.Clamp(start.Left + dx, 0, right - minWidth);
        if (_interactionKind.Contains("E")) right = Math.Clamp(right + dx, left + minWidth, 1);
        if (_interactionKind.Contains("N")) top = Math.Clamp(start.Top + dy, 0, bottom - minHeight);
        if (_interactionKind.Contains("S")) bottom = Math.Clamp(bottom + dy, top + minHeight, 1);

        return new SubtitleRegion(left, top, right - left, bottom - top);
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_interacting)
        {
            _interacting = false;
            SelectionCanvas.ReleaseMouseCapture();
            CommitRegion(_renderedRegion);
            e.Handled = true;
            return;
        }

        if (!_dragging) return;
        _dragging = false;
        SelectionCanvas.ReleaseMouseCapture();
        SelectionRect.Visibility = Visibility.Collapsed;

        var end = e.GetPosition(SelectionCanvas);
        var x = Math.Clamp((Math.Min(_start.X, end.X) - _imageLeft) / _area.Width, 0, 1);
        var y = Math.Clamp((Math.Min(_start.Y, end.Y) - _imageTop) / _area.Height, 0, 1);
        var width = Math.Clamp(Math.Abs(end.X - _start.X) / _area.Width, 0, 1 - x);
        var height = Math.Clamp(Math.Abs(end.Y - _start.Y) / _area.Height, 0, 1 - y);

        if (width >= 0.01 && height >= 0.01)
            CommitRegion(new SubtitleRegion(x, y, width, height));
        else
            DrawOutlines();
    }

    private void ApplyGeometry_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRegion is null) return;
        if (!TryPixels(LeftBox.Text, out var leftPixels) ||
            !TryPixels(TopBox.Text, out var topPixels) ||
            !TryPixels(WidthBox.Text, out var widthPixels) ||
            !TryPixels(HeightBox.Text, out var heightPixels))
        {
            SizeLabel.Text = "Inserisci X, Y, larghezza e altezza in pixel (es. 100, 200, 480, 180).";
            return;
        }

        widthPixels = Math.Clamp(widthPixels, 20, _area.Width);
        heightPixels = Math.Clamp(heightPixels, 20, _area.Height);
        leftPixels = Math.Clamp(leftPixels, 0, Math.Max(0, _area.Width - widthPixels));
        topPixels = Math.Clamp(topPixels, 0, Math.Max(0, _area.Height - heightPixels));

        CommitRegion(new SubtitleRegion(
            leftPixels / _area.Width,
            topPixels / _area.Height,
            widthPixels / _area.Width,
            heightPixels / _area.Height));
    }

    private static bool TryPixels(string? text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _regions.Count) return;
        _regions[_selectedIndex].Enabled = false;
        DrawOutlines();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
