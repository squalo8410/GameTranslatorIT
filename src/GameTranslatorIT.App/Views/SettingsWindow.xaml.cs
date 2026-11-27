using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameTranslatorIT.App.Services;
using GameTranslatorIT.Core.Models;
using GameTranslatorIT.Tts;
using Microsoft.Win32;

namespace GameTranslatorIT.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<ScreenRect>? _captureAreaProvider;
    private readonly Action<AudioOutputDevice>? _onDeviceChosen;
    private IReadOnlyList<AudioOutputDevice> _allAudioDevices = Array.Empty<AudioOutputDevice>();

    public SettingsWindow(AppSettings settings, Func<ScreenRect>? captureAreaProvider = null,
        Action<AudioOutputDevice>? onDeviceChosen = null)
    {
        InitializeComponent();
        _settings = settings;
        _captureAreaProvider = captureAreaProvider;
        _onDeviceChosen = onDeviceChosen;

        // Posizionamento esplicito: CenterOwner può aprire la finestra fuori schermo
        // quando la finestra principale è massimizzata o ripristinata fuori area utile.
        var work = SystemParameters.WorkArea;
        Left = Math.Max(work.Left, work.Left + (work.Width - Width) / 2);
        Top = Math.Max(work.Top, work.Top + (work.Height - Height) / 2);
        Topmost = true;
        Loaded += (_, _) =>
        {
            Topmost = false;
            Activate();
        };

        var ocrTags = LanguageCatalog.GetOcrLanguageTags();
        var initial = new AvailableLanguages(new HashSet<string>(), new HashSet<string>(), false);
        SourceLanguageCombo.ItemsSource = LanguageCatalog.BuildSourceOptions(
            ocrTags, initial, settings.SourceLanguage, settings.AllowOnlineFallbackForMissingLanguages);
        TargetLanguageCombo.ItemsSource = LanguageCatalog.BuildTargetOptions(
            ocrTags, initial, settings.TargetLanguage, settings.AllowOnlineFallbackForMissingLanguages);
        OcrLanguageCombo.ItemsSource = LanguageCatalog.BuildOcrOptions(ocrTags, EffectiveOcrTag(settings));

        DataContext = settings;
        GitHubTokenBox.Password = settings.GitHubToken ?? string.Empty;

        OverlayPositionCombo.ItemsSource = Enum.GetValues<OverlayPosition>();
        OverlayFallbackCombo.ItemsSource = Enum.GetValues<OverlayFallback>();
        RegionCountCombo.ItemsSource = Enumerable.Range(1, 12).ToArray();

        var voices = ItalianSpeechService.GetInstalledVoices();
        TtsVoiceCombo.ItemsSource = voices;
        TtsVoiceCombo.SelectedItem = voices.FirstOrDefault(v =>
                                         string.Equals(v.Name, settings.TtsVoiceName, StringComparison.OrdinalIgnoreCase))
                                     ?? voices.FirstOrDefault(v => v.Language.StartsWith("it", StringComparison.OrdinalIgnoreCase))
                                     ?? voices.FirstOrDefault();

        _ = LoadLanguagesAsync(ocrTags);
        _ = LoadAudioDevicesAsync();
        _ = LoadInputDevicesAsync();
    }

    private async Task LoadInputDevicesAsync()
    {
        var devices = await AudioInputDeviceService.GetDevicesAsync();
        await Dispatcher.InvokeAsync(() =>
        {
            AudioInputCombo.ItemsSource = devices;
            AudioInputCombo.SelectedItem = devices.FirstOrDefault(d =>
                                               string.Equals(d.Id, _settings.AudioInputDeviceId, StringComparison.OrdinalIgnoreCase))
                                           ?? devices.FirstOrDefault(d => d.IsDefault)
                                           ?? devices.FirstOrDefault(d => d.IsActive);
        });
    }

    private async Task LoadAudioDevicesAsync()
    {
        var devices = await AudioOutputDeviceService.GetDevicesAsync();

        await Dispatcher.InvokeAsync(() =>
        {
            _allAudioDevices = devices;
            ApplyAudioDeviceFilter();
        });
    }

    private void ApplyAudioDeviceFilter()
    {
        var visible = _settings.ShowOnlyActiveAudioDevices
            ? _allAudioDevices.Where(device => device.IsActive).ToList()
            : _allAudioDevices.ToList();

        AudioOutputCombo.ItemsSource = visible;
        var saved = _settings.AudioOutputDeviceId;
        var savedDevice = saved is null ? null : visible.FirstOrDefault(d =>
            string.Equals(d.Id, saved, StringComparison.OrdinalIgnoreCase));
        AudioOutputCombo.SelectedItem =
            savedDevice
            ?? visible.FirstOrDefault(d => d.IsDefault)
            ?? visible.FirstOrDefault(d => d.IsActive)
            ?? visible.FirstOrDefault();
    }

    private void RefreshAudioDevices_Click(object sender, RoutedEventArgs e)
        => _ = LoadAudioDevicesAsync();

    private void ActiveAudioFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true }) ApplyAudioDeviceFilter();
    }

    private void SelectRegionSetting_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CaptureRegionConfig region) return;
        _settings.EnsureCaptureRegions();

        if (_captureAreaProvider is null)
        {
            MessageBox.Show(this, "Area di cattura non disponibile.", "Selezione riquadro",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var area = _captureAreaProvider();
        if (area.Width <= 0 || area.Height <= 0)
        {
            MessageBox.Show(this, "Seleziona prima un monitor o una finestra valida.", "Selezione riquadro",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var index = _settings.CaptureRegions.IndexOf(region);
        var selector = new RegionSelectorWindow(area, _settings.CaptureRegions, index) { Owner = this };
        if (selector.ShowDialog() == true) _settings.UseCaptureRegions = true;
    }

    private void RemoveRegionSetting_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CaptureRegionConfig region)
            region.Enabled = false;
    }

    private async Task LoadLanguagesAsync(IReadOnlyList<string> ocrTags)
    {
        var available = await LanguageCatalog.QueryAsync(
            _settings.LocalTranslateBaseUrl, _settings.TranslationTimeoutMs);
        var allowOnline = _settings.AllowOnlineFallbackForMissingLanguages;

        // Acquisiamo i valori prima di toccare ItemsSource: il binding SelectedValue
        // è a due vie e, se l'elenco cambia, può riscrivere temporaneamente null.
        var currentSource = _settings.SourceLanguage;
        var currentTarget = _settings.TargetLanguage;
        var currentOcr = EffectiveOcrTag(_settings);

        var sources = LanguageCatalog.BuildSourceOptions(ocrTags, available, currentSource, allowOnline);
        var targets = LanguageCatalog.BuildTargetOptions(ocrTags, available, currentTarget, allowOnline);

        await Dispatcher.InvokeAsync(() =>
        {
            SourceLanguageCombo.ItemsSource = sources;
            TargetLanguageCombo.ItemsSource = targets;
            OcrLanguageCombo.ItemsSource = LanguageCatalog.BuildOcrOptions(ocrTags, currentOcr);
            SourceLanguageCombo.SelectedValue = currentSource;
            TargetLanguageCombo.SelectedValue = currentTarget;
            OcrLanguageCombo.SelectedValue = currentOcr;
            _settings.SourceLanguage = currentSource;
            _settings.TargetLanguage = currentTarget;

            var localTargets = targets.Count(o => o.Installed && !o.OnlineOnly);
            var onlineTargets = targets.Count(o => o.OnlineOnly && o.Installed);
            var onlineNote = allowOnline
                ? "Fallback online ATTIVO: islandese, croato, serbo, macedone, tamil, telugu, gallese e latino inviano il testo a un servizio online."
                : "Fallback online disattivato: le 8 lingue senza modello locale non sono traducibili finché non lo attivi.";

            LanguageInfoText.Text = available.TranslationServiceReachable
                ? $"{LanguageCatalog.TotalCount} lingue selezionabili · {localTargets} destinazioni locali pronte · {onlineTargets} online. Per la sorgente serve un riconoscitore OCR di Windows ({string.Join(", ", ocrTags)}). Con 'Rilevamento automatico' la lingua è indovinata dal testo, mentre la lettura usa la lingua OCR scelta. {onlineNote}"
                : $"LibreTranslate non è raggiungibile: {LanguageCatalog.TotalCount} lingue restano selezionabili, ma i modelli installati non sono verificabili. Avvia il servizio locale per aggiornare gli indicatori. {onlineNote}";
        });
    }

    private static string EffectiveOcrTag(AppSettings settings)
        => !string.Equals(settings.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
            ? settings.SourceLanguage
            : string.IsNullOrWhiteSpace(settings.OcrLanguage) ? "en-US" : settings.OcrLanguage;

    private void OnlineFallback_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = LoadLanguagesAsync(LanguageCatalog.GetOcrLanguageTags());
    }

    private void InstallOcrLanguages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:regionlanguage",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Impossibile aprire le impostazioni di Windows: " + ex.Message,
                "Lingue OCR", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowGitHubToken_Changed(object sender, RoutedEventArgs e)
    {
        GitHubTokenBox.PasswordChar = ShowGitHubTokenCheck.IsChecked == true ? '\0' : '●';
    }

    private void BrowseGlossary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Seleziona il file glossario",
            Filter = "File di testo (*.txt)|*.txt|Tutti i file (*.*)|*.*",
            CheckFileExists = false
        };

        if (dialog.ShowDialog() == true)
            _settings.GlossaryPath = dialog.FileName;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // ShowDialog restituisce null/false quando DialogResult non è stato impostato:
        // impostarlo esplicitamente può sollevare un'eccezione se la finestra non è
        // ancora entrata nel loop modale. Chiudere è sufficiente per "Annulla".
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.SourceLanguage) || string.IsNullOrWhiteSpace(_settings.TargetLanguage))
        {
            MessageBox.Show(this,
                "Seleziona una lingua sorgente e una lingua target dagli elenchi.",
                "Lingue non valide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(_settings.TargetLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                "Il rilevamento automatico può essere usato solo per la lingua sorgente.",
                "Lingue non valide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(_settings.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(_settings.OcrLanguage))
                _settings.OcrLanguage = "en-US";
        }
        else
        {
            // Con una sorgente esplicita la lettura OCR segue la lingua scelta.
            _settings.OcrLanguage = _settings.SourceLanguage;
        }

        if (!IsValidHex(TextColorBox.Text) || !IsValidHex(BackgroundColorBox.Text))
        {
            MessageBox.Show(this,
                "I colori devono essere in formato HEX, ad esempio #FFFFFFFF.",
                "Colore non valido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.EnsureCaptureRegions();
        _settings.SubtitleFontSize = Math.Clamp(_settings.SubtitleFontSize, 16, 64);
        _settings.BackgroundOpacity = Math.Clamp(_settings.BackgroundOpacity, 0, 1);
        _settings.UpdateIntervalMs = Math.Clamp(_settings.UpdateIntervalMs, 100, 2000);
        _settings.OcrSensitivity = Math.Clamp(_settings.OcrSensitivity, 0, 100);
        _settings.VoiceVolume = Math.Clamp(_settings.VoiceVolume, 0, 100);
        _settings.VoiceRate = Math.Clamp(_settings.VoiceRate, 50, 200);
        _settings.SystemVolume = Math.Clamp(_settings.SystemVolume, 0, 100);
        _settings.TranslationTimeoutMs = Math.Clamp(_settings.TranslationTimeoutMs, 500, 30000);
        _settings.UpdateCheckIntervalHours = Math.Clamp(_settings.UpdateCheckIntervalHours, 1, 168);
        _settings.UpdateManifestUrl = _settings.UpdateManifestUrl?.Trim() ?? string.Empty;
        _settings.LanguageInstallPythonPath = _settings.LanguageInstallPythonPath?.Trim() ?? string.Empty;
        _settings.ArgosPackagesDirectory = _settings.ArgosPackagesDirectory?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(_settings.UpdateManifestUrl) &&
            (!Uri.TryCreate(_settings.UpdateManifestUrl, UriKind.Absolute, out var manifestUri) ||
             manifestUri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show(this,
                "L'URL del manifest aggiornamenti deve essere HTTPS (oppure lasciare il campo vuoto).",
                "URL non valido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.TtsVoiceName = (TtsVoiceCombo.SelectedItem as SpeechVoice)?.Name;
        _settings.AudioInputDeviceId = (AudioInputCombo.SelectedItem as AudioInputDevice)?.Id;
        _settings.GitHubToken = GitHubTokenBox.Password.Trim();

        var audioDevice = AudioOutputCombo.SelectedItem as AudioOutputDevice;
        _settings.AudioOutputDeviceId = audioDevice?.Id;
        if (audioDevice is not null) _onDeviceChosen?.Invoke(audioDevice);
        var usableDeviceId = audioDevice is { VolumeAvailable: true } ? audioDevice.Id : null;

        try
        {
            new SystemVolumeController().SetVolume(_settings.SystemVolume, usableDeviceId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Impostazioni salvate, ma il volume audio non è stato applicato: " + ex.Message,
                "Volume audio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (!string.IsNullOrWhiteSpace(_settings.GlossaryPath) && !File.Exists(_settings.GlossaryPath))
        {
            var result = MessageBox.Show(this,
                "Il file glossario specificato non esiste. Salvare comunque le impostazioni?",
                "Glossario non trovato", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }

        DialogResult = true;
        Close();
    }

    private static bool IsValidHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            _ = ColorConverter.ConvertFromString(value.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
