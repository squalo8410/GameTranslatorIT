using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameTranslatorIT.Core.Models;

/// <summary>
/// Impostazioni persistenti dell'applicazione. Serializzate in JSON nella cartella AppData.
/// </summary>
public sealed class AppSettings
{
    public AppSettings()
    {
        EnsureCaptureRegions();
    }

    public string SourceLanguage { get; set; } = "en-US";
    public string TargetLanguage { get; set; } = "it-IT";

    /// <summary>
    /// Lingua del riconoscitore OCR di Windows. Vuota = usa SourceLanguage, per
    /// compatibilità con le impostazioni salvate prima del rilevamento automatico.
    /// </summary>
    public string OcrLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Consente il fallback online solo per le lingue senza modello Argos locale.
    /// Disattivato di default: la traduzione resta completamente locale.
    /// </summary>
    public bool AllowOnlineFallbackForMissingLanguages { get; set; }

    /// <summary>Rileva la lingua dal testo letto invece di imporla (source=auto).</summary>
    [JsonIgnore]
    public bool AutoDetectSourceLanguage
    {
        get => string.Equals(SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) SourceLanguage = "auto";
            else if (string.Equals(SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
                SourceLanguage = string.IsNullOrWhiteSpace(OcrLanguage) ? "en-US" : OcrLanguage;
        }
    }

    public CaptureMode CaptureMode { get; set; } = CaptureMode.FullScreen;
    public int SelectedMonitorIndex { get; set; }

    /// <summary>Identificativi stabili della finestra di gioco. Il titolo resta solo come etichetta.</summary>
    public long SelectedWindowHandle { get; set; }
    public int SelectedWindowPid { get; set; }
    public string? SelectedWindowTitle { get; set; }

    /// <summary>Rileva automaticamente la finestra in primo piano quando non c'è una scelta salvata.</summary>
    public bool AutoDetectGameWindow { get; set; } = true;

    public int SubtitleFontSize { get; set; } = 28;
    public string TextColor { get; set; } = "#FFFFFFFF";
    public string BackgroundColor { get; set; } = "#CC000000";
    public double BackgroundOpacity { get; set; } = 0.75;
    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.SopraSottotitolo;
    public bool ShowOriginalAndTranslation { get; set; }

    /// <summary>Geometria salvata quando l'overlay è in posizione personalizzata.</summary>
    public double OverlayLeft { get; set; } = 996;
    public double OverlayTop { get; set; } = 24;
    public double OverlayWidth { get; set; } = 900;
    public double OverlayHeight { get; set; } = 180;

    public int UpdateIntervalMs { get; set; } = 250;
    public int OcrSensitivity { get; set; } = 70;
    public int OcrUpscalePercent { get; set; } = 200;

    /// <summary>
    /// Se true usa l'area sottotitoli definita manualmente invece di quella automatica.
    /// </summary>
    public bool UseManualSubtitleRegion { get; set; }
    public double ManualRegionLeft { get; set; }
    public double ManualRegionTop { get; set; } = 0.68;
    public double ManualRegionWidth { get; set; } = 1;
    public double ManualRegionHeight { get; set; } = 0.30;

    /// <summary>Usa i riquadri selezionati invece dell'unica area sottotitoli.</summary>
    public bool UseCaptureRegions { get; set; } = true;

    /// <summary>Numero di riquadri selezionabili (4 predefiniti, 6, o numero personalizzato).</summary>
    public int RegionCount { get; set; } = 4;

    /// <summary>Abilita i tasti rapidi globali per selezione e muto dei riquadri.</summary>
    public bool HotkeysEnabled { get; set; } = true;

    /// <summary>Mostra il pannello di controllo rapido all'avvio dell'app.</summary>
    public bool QuickControlAlwaysVisible { get; set; }

    /// <summary>Riquadri di lettura. Vengono creati automaticamente se mancanti.</summary>
    public ObservableCollection<CaptureRegionConfig> CaptureRegions { get; set; } = new();

    /// <summary>Lettura a voce alta dei sottotitoli tradotti.</summary>
    public bool VoiceEnabled { get; set; } = true;

    /// <summary>Volume della voce tradotta (0-100).</summary>
    public int VoiceVolume { get; set; } = 80;

    /// <summary>Velocità della voce tradotta (50-200%).</summary>
    public int VoiceRate { get; set; } = 100;

    /// <summary>Volume di sistema Windows mostrato e modificato dall'app (0-100).</summary>
    public int SystemVolume { get; set; } = 80;

    /// <summary>ID del dispositivo audio di uscita scelto (null = predefinito di Windows).</summary>
    public string? AudioOutputDeviceId { get; set; }

    /// <summary>ID del dispositivo di ingresso (microfono) scelto, se presente.</summary>
    public string? AudioInputDeviceId { get; set; }

    /// <summary>Riproduzione della voce su tutte le uscite audio attive.</summary>
    public bool PlayOnAllAudioDevices { get; set; }

    /// <summary>Nome della voce Windows usata per la sintesi (null = prima voce italiana).</summary>
    public string? TtsVoiceName { get; set; }

    /// <summary>Mostra solo le uscite audio attive nell'elenco, così la scelta è immediata.</summary>
    public bool ShowOnlyActiveAudioDevices { get; set; } = true;

    /// <summary>Uscita che l'utente ha chiesto di collegare: viene selezionata automaticamente quando diventa attiva.</summary>
    public string? PendingAudioDeviceName { get; set; }

    public TranslationProviderKind TranslationProvider { get; set; } = TranslationProviderKind.LocalLibreTranslate;
    public string LocalTranslateBaseUrl { get; set; } = "http://127.0.0.1:5000";
    public int TranslationTimeoutMs { get; set; } = 4000;

    /// <summary>
    /// Se true, l'app mostra un avviso quando rileva la modalità schermo intero esclusivo.
    /// </summary>
    public bool WarnOnExclusiveFullscreen { get; set; } = true;

    /// <summary>
    /// Modalità di fallback quando l'overlay non può essere mostrato sopra il gioco.
    /// </summary>
    public OverlayFallback OverlayFallback { get; set; } = OverlayFallback.MonitorSecondario;

    public string GlossaryPath { get; set; } = string.Empty;

    /// <summary>Controlla GitHub Releases all'avvio dell'app.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>Intervallo del controllo periodico degli aggiornamenti, in ore (1-168).</summary>
    public int UpdateCheckIntervalHours { get; set; } = 6;

    /// <summary>Ultimo controllo versione eseguito con successo.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Versione che l'utente ha scelto di ignorare.</summary>
    public string? SkippedVersion { get; set; }

    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Copia completa usata dalla finestra impostazioni per rendere "Annulla" effettivo.</summary>
    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, CloneOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, CloneOptions)
            ?? throw new InvalidOperationException("Copia delle impostazioni non riuscita.");
    }

    /// <summary>
    /// Copia tutti i valori in questa istanza, preservando i riferimenti usati
    /// da pipeline, overlay e binding della UI.
    /// </summary>
    public void CopyFrom(AppSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);

        foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0) continue;
            if (property.Name == nameof(CaptureRegions)) continue;
            property.SetValue(this, property.GetValue(source));
        }

        CaptureRegions ??= new ObservableCollection<CaptureRegionConfig>();
        CaptureRegions.Clear();
        foreach (var region in source.CaptureRegions)
            CaptureRegions.Add(region);
        EnsureCaptureRegions();
    }

    /// <summary>
    /// Allinea la lista dei riquadri al numero richiesto, preservando quelli esistenti.
    /// Da chiamare dopo il caricamento e ogni volta che il conteggio cambia.
    /// </summary>
    public void EnsureCaptureRegions()
    {
        RegionCount = Math.Clamp(RegionCount, 1, 12);
        CaptureRegions ??= new ObservableCollection<CaptureRegionConfig>();

        while (CaptureRegions.Count < RegionCount)
            CaptureRegions.Add(CaptureRegionConfig.CreateDefault(CaptureRegions.Count));

        while (CaptureRegions.Count > RegionCount)
            CaptureRegions.RemoveAt(CaptureRegions.Count - 1);
    }
}

public enum CaptureMode
{
    FullScreen,
    GameWindow
}

public enum OverlayPosition
{
    SopraSottotitolo,
    InAltoASinistra,
    InAltoADestra,
    InBassoASinistra,
    InBassoADestra,
    Personalizzata
}

public enum OverlayFallback
{
    SoloAvviso,
    MonitorSecondario,

    // Valori storici conservati per compatibilità con impostazioni già salvate:
    //SecondaryMonitorAndVoice usa il monitor secondario e la voce; SoloVoce usa la voce.
    SecondaryMonitorAndVoice,
    SoloVoce
}

public enum TranslationProviderKind
{
    LocalLibreTranslate
}
