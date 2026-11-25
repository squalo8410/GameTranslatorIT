using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Overlay;

/// <summary>
/// Finestra overlay layered, sempre in primo piano e trasparente ai click.
/// Ogni riquadro di lettura ha una propria riga con etichetta e stato del muto.
/// In modalità modifica il click-through viene disattivato per consentire spostamento e ridimensionamento.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private readonly AppSettings _settings;
    private readonly Action? _geometryChanged;
    private readonly ObservableCollection<OverlayRegionEntry> _entries = new();
    private bool _editMode;
    private bool _resizing;
    private Point _resizeStart;
    private (double W, double H) _startSize;
    private ScreenRect _targetArea;

    public OverlayWindow(AppSettings settings, Action? geometryChanged = null)
    {
        InitializeComponent();
        _settings = settings;
        _geometryChanged = geometryChanged;
        RegionItems.ItemsSource = _entries;
        ApplySettings();
        SourceInitialized += OnSourceInitialized;
    }

    public bool IsEditMode => _editMode;

    public void SetTargetArea(ScreenRect area) => _targetArea = area;

    public void ApplySettings()
    {
        var fontSize = Math.Clamp(_settings.SubtitleFontSize, 16, 64);
        var textColor = ParseColor(_settings.TextColor, Colors.White);
        var brush = new SolidColorBrush(textColor);
        var accent = new SolidColorBrush(Color.FromArgb(255, 255, 175, 70));

        foreach (var entry in _entries)
        {
            entry.FontSize = fontSize;
            entry.LineHeight = fontSize * 1.25;
            entry.OriginalFontSize = fontSize * 0.58;
            entry.Foreground = brush;
            entry.HeaderBrush = accent;
            entry.ShowOriginal = _settings.ShowOriginalAndTranslation;
        }

        var background = ParseColor(_settings.BackgroundColor, Color.FromArgb(200, 0, 0, 0));
        var alpha = (byte)Math.Clamp(Math.Round(_settings.BackgroundOpacity * 255), 0, 255);
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B));

        PositionWindow();
    }

    /// <summary>Mostra o aggiorna la traduzione di un singolo riquadro.</summary>
    public void ShowTranslation(
        string original,
        string translated,
        bool showOriginal,
        string regionId,
        string regionName,
        bool muted,
        string modeLabel)
    {
        var entry = _entries.FirstOrDefault(e => string.Equals(e.RegionId, regionId, StringComparison.Ordinal));
        if (entry is null)
        {
            entry = new OverlayRegionEntry { RegionId = regionId };
            _entries.Add(entry);
        }

        entry.Name = regionName;
        entry.Original = original;
        entry.Translated = translated;
        entry.ShowOriginal = showOriginal;
        entry.Muted = muted;
        entry.ModeLabel = modeLabel;
        ApplySettings();
    }

    /// <summary>Aggiorna soltanto l'etichetta di muto di un riquadro già mostrato.</summary>
    public void UpdateRegionState(string regionId, string regionName, bool muted, string modeLabel)
    {
        var entry = _entries.FirstOrDefault(e => string.Equals(e.RegionId, regionId, StringComparison.Ordinal));
        if (entry is null) return;
        entry.Name = regionName;
        entry.Muted = muted;
        entry.ModeLabel = modeLabel;
        PositionWindow();
    }

    public void RemoveRegionEntry(string regionId)
    {
        var entry = _entries.FirstOrDefault(e => string.Equals(e.RegionId, regionId, StringComparison.Ordinal));
        if (entry is null) return;
        _entries.Remove(entry);
        PositionWindow();
    }

    public void ClearTranslations()
    {
        _entries.Clear();
        PositionWindow();
    }

    public void SetEditMode(bool enabled)
    {
        _editMode = enabled;
        ResizeGrip.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled) SizeToContent = SizeToContent.Manual;
        ApplyClickThrough(!enabled);
    }

    /// <summary>Applica posizione e dimensioni scelte numericamente dall'utente.</summary>
    public void ApplyCustomGeometry(double left, double top, double width, double height)
    {
        _settings.OverlayPosition = OverlayPosition.Personalizzata;
        _settings.OverlayLeft = left;
        _settings.OverlayTop = top;
        _settings.OverlayWidth = Math.Clamp(width, 360, 1600);
        _settings.OverlayHeight = Math.Clamp(height, 90, 520);
        SizeToContent = SizeToContent.Manual;
        Width = _settings.OverlayWidth;
        Height = _settings.OverlayHeight;
        Left = left;
        Top = top;
    }

    /// <summary>Salva la geometria dopo uno spostamento o un ridimensionamento con il mouse.</summary>
    private void SaveGeometry()
    {
        _settings.OverlayPosition = OverlayPosition.Personalizzata;
        _settings.OverlayLeft = Left;
        _settings.OverlayTop = Top;
        _settings.OverlayWidth = ActualWidth;
        _settings.OverlayHeight = ActualHeight;
        _geometryChanged?.Invoke();
    }

    public void PositionWindow()
    {
        if (_targetArea.Width <= 0 || _targetArea.Height <= 0) return;

        UpdateLayout();

        if (_settings.OverlayPosition == OverlayPosition.Personalizzata)
            SizeToContent = SizeToContent.Manual;

        double left;
        double top;
        const double margin = 24;

        switch (_settings.OverlayPosition)
        {
            case OverlayPosition.InAltoASinistra:
                left = _targetArea.Left + margin;
                top = _targetArea.Top + margin;
                break;
            case OverlayPosition.InAltoADestra:
                left = _targetArea.Right - Width - margin;
                top = _targetArea.Top + margin;
                break;
            case OverlayPosition.InBassoASinistra:
                left = _targetArea.Left + margin;
                top = _targetArea.Bottom - ActualHeight - margin;
                break;
            case OverlayPosition.InBassoADestra:
                left = _targetArea.Right - Width - margin;
                top = _targetArea.Bottom - ActualHeight - margin;
                break;
            case OverlayPosition.SopraSottotitolo:
            default:
                left = _targetArea.Left + (_targetArea.Width - Width) / 2.0;
                top = _targetArea.Bottom - ActualHeight - 48;
                break;
            case OverlayPosition.Personalizzata:
                Width = Math.Clamp(_settings.OverlayWidth, 360, 1600);
                Height = Math.Clamp(_settings.OverlayHeight, 90, 520);
                Left = _settings.OverlayLeft;
                Top = _settings.OverlayTop;
                return;
        }

        Left = left;
        Top = top;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style |= WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcHitTest && !_editMode)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }
        return IntPtr.Zero;
    }

    private void ApplyClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style = enabled
            ? style | WsExTransparent
            : style & ~WsExTransparent;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editMode && e.ClickCount == 1)
        {
            DragMove();
            SaveGeometry();
        }
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;
        SizeToContent = SizeToContent.Manual;
        _resizing = true;
        _resizeStart = PointToScreen(e.GetPosition(this));
        _startSize = (Width, ActualHeight);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;
        var current = PointToScreen(e.GetPosition(this));
        Width = Math.Max(360, _startSize.W + current.X - _resizeStart.X);
        Height = Math.Max(90, _startSize.H + current.Y - _resizeStart.Y);
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        SaveGeometry();
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return fallback;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
