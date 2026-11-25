using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameTranslatorIT.Core.Models;

/// <summary>Comportamento della muto quando un riquadro è silenziato.</summary>
public enum RegionMuteMode
{
    /// <summary>OCR, traduzione e overlay continuano; viene silenziata solo la voce.</summary>
    SoloVoce,

    /// <summary>Il riquadro viene sospeso del tutto: nessuna lettura e nessun aggiornamento.</summary>
    Blocca,

    /// <summary>Il testo cambiato viene accumulato e letto quando si toglie il muto.</summary>
    Accoda
}

/// <summary>
/// Un riquadro selezionato dall'utente. Le coordinate sono relative (0..1)
/// all'area di cattura, quindi restano valide se la finestra si sposta.
/// </summary>
public sealed class CaptureRegionConfig : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "Riquadro";
    private double _left;
    private double _top = 0.68;
    private double _width = 1;
    private double _height = 0.30;
    private bool _enabled = true;
    private bool _muted;
    private RegionMuteMode _muteMode = RegionMuteMode.SoloVoce;

    public string Id
    {
        get => _id;
        set => Set(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public double Left
    {
        get => _left;
        set => Set(ref _left, value);
    }

    public double Top
    {
        get => _top;
        set => Set(ref _top, value);
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => Set(ref _height, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public bool Muted
    {
        get => _muted;
        set => Set(ref _muted, value);
    }

    public RegionMuteMode MuteMode
    {
        get => _muteMode;
        set => Set(ref _muteMode, value);
    }

    public SubtitleRegion ToSubtitleRegion() => new(Left, Top, Width, Height);

    public void Set(SubtitleRegion region)
    {
        Left = Math.Clamp(region.Left, 0, 1);
        Top = Math.Clamp(region.Top, 0, 1);
        Width = Math.Clamp(region.Width, 0.01, 1 - Left);
        Height = Math.Clamp(region.Height, 0.01, 1 - Top);
        OnPropertyChanged(nameof(Coordinates));
    }

    public string Coordinates => $"{Left:0.00}, {Top:0.00} — {Width:0.00} × {Height:0.00}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (name is nameof(Left) or nameof(Top) or nameof(Width) or nameof(Height))
            OnPropertyChanged(nameof(Coordinates));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static CaptureRegionConfig CreateDefault(int index) => index switch
    {
        0 => new CaptureRegionConfig
        {
            Name = "Sottotitoli",
            Left = 0,
            Top = 0.68,
            Width = 1,
            Height = 0.30,
            Enabled = true,
            MuteMode = RegionMuteMode.SoloVoce
        },
        1 => new CaptureRegionConfig
        {
            Name = "Missioni",
            Left = 0.02,
            Top = 0.10,
            Width = 0.36,
            Height = 0.32,
            Enabled = false,
            Muted = true,
            MuteMode = RegionMuteMode.Accoda
        },
        2 => new CaptureRegionConfig
        {
            Name = "HUD",
            Left = 0.60,
            Top = 0.05,
            Width = 0.38,
            Height = 0.25,
            Enabled = false,
            MuteMode = RegionMuteMode.SoloVoce
        },
        _ => new CaptureRegionConfig
        {
            Name = $"Riquadro {index + 1}",
            Left = 0.30,
            Top = 0.25,
            Width = 0.40,
            Height = 0.30,
            Enabled = false,
            MuteMode = RegionMuteMode.SoloVoce
        }
    };
}
