using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace GameTranslatorIT.Overlay;

/// <summary>Riga dell'overlay associata a un riquadro di lettura.</summary>
public sealed class OverlayRegionEntry : INotifyPropertyChanged
{
    private string _regionId = string.Empty;
    private string _name = string.Empty;
    private string _original = string.Empty;
    private string _translated = string.Empty;
    private string _modeLabel = string.Empty;
    private bool _muted;
    private bool _showOriginal;
    private double _fontSize = 28;
    private double _originalFontSize = 16;
    private double _lineHeight = 35;
    private Brush _foreground = Brushes.White;
    private Brush _headerBrush = new SolidColorBrush(Color.FromArgb(255, 255, 170, 60));

    public string RegionId
    {
        get => _regionId;
        set => Set(ref _regionId, value);
    }

    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value)) OnPropertyChanged(nameof(Header)); }
    }

    public string Original
    {
        get => _original;
        set
        {
            if (Set(ref _original, value)) OnPropertyChanged(nameof(OriginalFlow));
        }
    }

    public string Translated
    {
        get => _translated;
        set
        {
            if (Set(ref _translated, value)) OnPropertyChanged(nameof(TextFlow));
        }
    }

    /// <summary>
    /// Elenco di font con fallback per alfabeti non latini: WPF sceglie il primo
    /// che contiene il glifo, evitando i quadratini con cinese, giapponese, arabo, ecc.
    /// </summary>
    public FontFamily UnicodeFont { get; } = new(
        "Segoe UI, Microsoft YaHei UI, Microsoft YaHei, Arial Unicode MS, Tahoma, SimSun, Yu Gothic, Malgun Gothic, Noto Sans");

    /// <summary>Testo tradotto da destra a sinistra quando contiene caratteri RTL.</summary>
    public FlowDirection TextFlow => ContainsRtl(Translated)
        ? FlowDirection.RightToLeft
        : FlowDirection.LeftToRight;

    /// <summary>Testo originale da destra a sinistra quando contiene caratteri RTL.</summary>
    public FlowDirection OriginalFlow => ContainsRtl(Original)
        ? FlowDirection.RightToLeft
        : FlowDirection.LeftToRight;

    private static bool ContainsRtl(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u0590' and <= '\u08FF' || c is >= '\uFB1D' and <= '\uFEFC')
                return true;
        }
        return false;
    }

    public bool Muted
    {
        get => _muted;
        set { if (Set(ref _muted, value)) OnPropertyChanged(nameof(Header)); }
    }

    public string ModeLabel
    {
        get => _modeLabel;
        set { if (Set(ref _modeLabel, value)) OnPropertyChanged(nameof(Header)); }
    }

    public string Header => Muted
        ? $"{Name}  ·  MUTO ({ModeLabel})"
        : Name;

    public bool ShowOriginal
    {
        get => _showOriginal;
        set { if (Set(ref _showOriginal, value)) OnPropertyChanged(nameof(OriginalVisibility)); }
    }

    public Visibility OriginalVisibility => ShowOriginal && !string.IsNullOrWhiteSpace(Original)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, value);
    }

    public double OriginalFontSize
    {
        get => _originalFontSize;
        set => Set(ref _originalFontSize, value);
    }

    public double LineHeight
    {
        get => _lineHeight;
        set => Set(ref _lineHeight, value);
    }

    public Brush Foreground
    {
        get => _foreground;
        set => Set(ref _foreground, value);
    }

    public Brush HeaderBrush
    {
        get => _headerBrush;
        set => Set(ref _headerBrush, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
