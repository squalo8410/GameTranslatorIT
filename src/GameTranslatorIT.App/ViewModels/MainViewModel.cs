using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using GameTranslatorIT.App.Commands;
using GameTranslatorIT.App.Services;
using GameTranslatorIT.App.Views;
using GameTranslatorIT.Capture;
using GameTranslatorIT.Core.Models;
using GameTranslatorIT.Core.Services;
using GameTranslatorIT.Overlay;
using GameTranslatorIT.Translation;
using GameTranslatorIT.Tts;

namespace GameTranslatorIT.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ItalianSpeechService _speech = new();
    private readonly SystemVolumeController _systemVolumeController = new();
    private readonly OverlayWindow _overlay;

    private TranslationPipeline? _pipeline;
    private ITranslationProvider _provider;
    private AvailableLanguages? _lastAvailableLanguages;
    private string _languageHint = "Lingue in caricamento…";
    private DispatcherTimer? _languageRefreshTimer;
    private bool _languageRefreshRunning;
    private bool _autoStartAttempted;
    private bool _isRefreshingLanguageOptions;
    private bool _isRunning;
    private bool _forceVoice;
    private bool _disposed;
    private int _systemVolume;
    private AudioOutputDevice? _selectedAudioOutputDevice;
    private AudioOutputDevice? _chosenAudioOutputDevice;
    private bool _suppressAudioDeviceApply;
    private bool _suppressChosenApply;
    private readonly Dictionary<string, Queue<string>> _pendingSpeech = new(StringComparer.OrdinalIgnoreCase);
    private string? _speakingRegionId;
    private GlobalHotkeyService? _hotkeys;
    private IntPtr _hotkeyHwnd;
    private ControlPanelWindow? _controlPanel;
    private QuickControlWindow? _quickControl;
    private bool _systemMuted;
    private DispatcherTimer? _pendingDeviceTimer;
    private int _subtitleCount;
    private int _translationCount;
    private string _statusText = "Pronto";
    private string _lastSourceText = "—";
    private string _lastTranslatedText = "—";
    private ComponentStatus _captureStatus = new(ComponentState.NonInizializzato, "Non avviato");
    private ComponentStatus _ocrStatus = new(ComponentState.NonInizializzato, "Non avviato");
    private ComponentStatus _translationStatus = new(ComponentState.NonInizializzato, "Non avviato");
    private ComponentStatus _voiceStatus = new(ComponentState.NonInizializzato, "Non inizializzato");
    private ComponentStatus _updateStatus = new(ComponentState.NonInizializzato, "Non verificato");
    private string _updateStatusText = $"Versione {UpdateService.CurrentVersion} — aggiornamenti non verificati";
    private readonly UpdateService _updateService = new();
    private System.Threading.Timer? _updateTimer;
    private UpdateWindow? _updateWindow;
    private bool _updateCheckRunning;
    private bool _updateApplyRunning;

    public AppSettings Settings { get; }

    public ObservableCollection<MonitorInfo> Monitors { get; } = new();
    public ObservableCollection<WindowInfo> Windows { get; } = new();
    private ObservableCollection<LanguageOption> _sourceLanguageOptions = new();
    private ObservableCollection<LanguageOption> _targetLanguageOptions = new();

    /// <summary>
    /// La collection viene sostituita (non svuotata) quando si ricarica l'elenco:
    /// svuotarla farebbe scrivere null nel binding SelectedValue a due vie.
    /// </summary>
    public ObservableCollection<LanguageOption> SourceLanguageOptions
    {
        get => _sourceLanguageOptions;
        private set => SetField(ref _sourceLanguageOptions, value);
    }

    public ObservableCollection<LanguageOption> TargetLanguageOptions
    {
        get => _targetLanguageOptions;
        private set => SetField(ref _targetLanguageOptions, value);
    }
    public ObservableCollection<AudioOutputDevice> AudioOutputDevices { get; } = new();
    public ObservableCollection<string> LogEntries { get; } = new();

    /// <summary>Elenco mostrato, filtrato se l'utente vuole vedere solo le uscite attive.</summary>
    public IEnumerable<AudioOutputDevice> VisibleAudioOutputDevices => Settings.ShowOnlyActiveAudioDevices
        ? AudioOutputDevices.Where(device => device.IsActive)
        : AudioOutputDevices;

    /// <summary>Riquadri selezionati (la stessa collezione persistente delle impostazioni).</summary>
    public ObservableCollection<CaptureRegionConfig> Regions => Settings.CaptureRegions;

    public bool UseCaptureRegions
    {
        get => Settings.UseCaptureRegions;
        set
        {
            if (Settings.UseCaptureRegions == value) return;
            Settings.UseCaptureRegions = value;
            OnPropertyChanged(nameof(UseCaptureRegions));
            SaveAndRestartIfRunning(value
                ? "Lettura a riquadri attivata."
                : "Lettura a riquadri disattivata: usata l'area sottotitoli unica.");
        }
    }

    public int RegionCount
    {
        get => Settings.RegionCount;
        set
        {
            var clamped = Math.Clamp(value, 1, 12);
            if (Settings.RegionCount == clamped) return;
            Settings.RegionCount = clamped;
            Settings.EnsureCaptureRegions();
            OnPropertyChanged(nameof(RegionCount));
            OnPropertyChanged(nameof(Regions));
            SaveAndRestartIfRunning($"Numero di riquadri impostato: {clamped}.");
        }
    }

    public bool HotkeysEnabled
    {
        get => Settings.HotkeysEnabled;
        set
        {
            if (Settings.HotkeysEnabled == value) return;
            Settings.HotkeysEnabled = value;
            OnPropertyChanged(nameof(HotkeysEnabled));
            _settingsService.Save(Settings);
            RefreshHotkeys();
        }
    }

    public string RegionsSummary
    {
        get
        {
            var active = Settings.CaptureRegions.Count(r => r.Enabled);
            var muted = Settings.CaptureRegions.Count(r => r.Enabled && r.Muted);
            return $"{active} attivi · {muted} silenziati su {Settings.CaptureRegions.Count}";
        }
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand RefreshLanguagesCommand { get; }
    public RelayCommand RefreshAudioDevicesCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand TestTranslationCommand { get; }
    public RelayCommand ToggleVoiceCommand { get; }
    public RelayCommand ToggleOverlayEditCommand { get; }
    public RelayCommand ToggleRegionsCommand { get; }
    public RelayCommand SelectRegionCommand { get; }
    public RelayCommand ToggleRegionMuteCommand { get; }
    public RelayCommand CycleRegionModeCommand { get; }
    public RelayCommand RemoveRegionCommand { get; }
    public RelayCommand AddRegionCommand { get; }
    public RelayCommand OpenControlPanelCommand { get; }
    public RelayCommand OpenAudioDeviceWindowCommand { get; }
    public RelayCommand OpenOverlayGeometryWindowCommand { get; }
    public RelayCommand ToggleQuickControlCommand { get; }
    public RelayCommand ToggleSystemMuteCommand { get; }
    public RelayCommand TestVoiceCommand { get; }
    public RelayCommand CheckForUpdatesCommand { get; }

    /// <summary>Valori disponibili per il numero di riquadri nel pannello di controllo.</summary>
    public IEnumerable<int> RegionCounts => Enumerable.Range(1, 12);

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        Settings = settingsService.Load();
        _provider = CreateProvider();
        _overlay = new OverlayWindow(Settings, () => _settingsService.Save(Settings));

        StartCommand = new RelayCommand(_ => Start(), _ => !IsRunning);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning);
        RefreshDevicesCommand = new RelayCommand(_ => RefreshDevices());
        RefreshLanguagesCommand = new RelayCommand(async _ => await RefreshLanguagesAsync());
        RefreshAudioDevicesCommand = new RelayCommand(async _ => await RefreshAudioDevicesAsync());
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        TestTranslationCommand = new RelayCommand(async _ => await TestTranslationAsync());
        ToggleVoiceCommand = new RelayCommand(_ => ToggleVoice());
        ToggleOverlayEditCommand = new RelayCommand(_ => ToggleOverlayEdit());
        ToggleRegionsCommand = new RelayCommand(_ => ToggleRegions());
        SelectRegionCommand = new RelayCommand(p => SelectRegion(ResolveRegion(p)));
        ToggleRegionMuteCommand = new RelayCommand(p => ToggleRegionMute(ResolveRegion(p)));
        CycleRegionModeCommand = new RelayCommand(p => CycleRegionMode(ResolveRegion(p)));
        RemoveRegionCommand = new RelayCommand(p => RemoveRegion(ResolveRegion(p)));
        AddRegionCommand = new RelayCommand(_ => AddRegion());
        OpenControlPanelCommand = new RelayCommand(_ => OpenControlPanel());
        OpenAudioDeviceWindowCommand = new RelayCommand(_ => OpenAudioDeviceWindow());
        OpenOverlayGeometryWindowCommand = new RelayCommand(_ => OpenOverlayGeometryWindow());
        ToggleQuickControlCommand = new RelayCommand(_ => ToggleQuickControl());
        ToggleSystemMuteCommand = new RelayCommand(_ => ToggleSystemMute());
        TestVoiceCommand = new RelayCommand(async _ => await TestVoiceAsync());
        CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync(true, false));

        RefreshDevices();
        StartLanguageRefreshTimer();
        _ = LoadLanguageOptionsAsync();

        try
        {
            _systemVolume = _systemVolumeController.GetVolume();
            Settings.SystemVolume = _systemVolume;
            AppendLog($"Volume Windows rilevato: {_systemVolume}%.");
        }
        catch (Exception ex)
        {
            _systemVolume = Math.Clamp(Settings.SystemVolume, 0, 100);
            AppendLog($"Volume Windows non disponibile: {ex.Message}");
        }

        try
        {
            _systemMuted = _systemVolumeController.GetMute();
        }
        catch (Exception ex)
        {
            AppendLog($"Stato muto Windows non disponibile: {ex.Message}");
        }

        _speech.Initialize();
        EvaluateVoiceStatus();
        _ = RefreshAudioDevicesAsync();

        if (!string.IsNullOrWhiteSpace(Settings.PendingAudioDeviceName))
            StartPendingDeviceWatch();

        if (Settings.WarnOnExclusiveFullscreen)
            AppendLog("Nota: in schermo intero esclusivo l'overlay in-game non è garantito. Usa 'finestra senza bordi' quando possibile.");

        AppendLog($"Versione installata: {CurrentVersion}.");
        if (Settings.CheckForUpdatesOnStartup)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                await CheckForUpdatesAsync(true, true).ConfigureAwait(false);
            });
            StartUpdateTimer();
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetField(ref _isRunning, value)) return;
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>Riepilogo del motore effettivamente usato per la coppia di lingue scelta.</summary>
    public string LanguageHint
    {
        get => _languageHint;
        private set => SetField(ref _languageHint, value);
    }

    public string LastSourceText
    {
        get => _lastSourceText;
        private set => SetField(ref _lastSourceText, value);
    }

    public string LastTranslatedText
    {
        get => _lastTranslatedText;
        private set => SetField(ref _lastTranslatedText, value);
    }

    public int SubtitleCount
    {
        get => _subtitleCount;
        private set => SetField(ref _subtitleCount, value);
    }

    public int TranslationCount
    {
        get => _translationCount;
        private set => SetField(ref _translationCount, value);
    }

    public ComponentStatus CaptureStatus
    {
        get => _captureStatus;
        private set => SetField(ref _captureStatus, value);
    }

    public ComponentStatus OcrStatus
    {
        get => _ocrStatus;
        private set => SetField(ref _ocrStatus, value);
    }

    public ComponentStatus TranslationStatus
    {
        get => _translationStatus;
        private set => SetField(ref _translationStatus, value);
    }

    public ComponentStatus VoiceStatus
    {
        get => _voiceStatus;
        private set => SetField(ref _voiceStatus, value);
    }

    public ComponentStatus UpdateStatus
    {
        get => _updateStatus;
        // Il controllo aggiornamenti gira su un thread di background: la notifica
        // di proprietà deve essere marshallata sul dispatcher dell'UI.
        private set => Dispatch(() => SetField(ref _updateStatus, value));
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => Dispatch(() => SetField(ref _updateStatusText, value));
    }

    public string CurrentVersion => UpdateService.CurrentVersion;

    /// <summary>Dispositivo di uscita audio usato dalla voce e dal controllo volume.</summary>
    public AudioOutputDevice? SelectedAudioOutputDevice
    {
        get => _selectedAudioOutputDevice;
        set
        {
            if (value is { IsActive: false })
            {
                AppendLog($"Dispositivo non ancora attivo: {value.Name} ({value.Status}). Usa 'Collega / usa questo dispositivo' per richiederlo.");
                // La notifica con lo stesso valore non fa aggiornare la ComboBox:
                // passa per null e torna al dispositivo valido per forzare il ripristino visivo.
                var valid = _selectedAudioOutputDevice;
                if (valid is not null)
                {
                    _selectedAudioOutputDevice = null;
                    OnPropertyChanged(nameof(SelectedAudioOutputDevice));
                    _selectedAudioOutputDevice = valid;
                }
                OnPropertyChanged(nameof(SelectedAudioOutputDevice));
                return;
            }

            if (!SetField(ref _selectedAudioOutputDevice, value)) return;
            if (_suppressAudioDeviceApply) return;

            // Una scelta manuale diversa annulla l'uscita lasciata in attesa.
            if (value is not null && Settings.PendingAudioDeviceName is { Length: > 0 } pending &&
                !DeviceNameMatches(value.Name, pending))
            {
                SetPendingDevice(null);
                StopPendingDeviceWatch();
                AppendLog("Uscita in attesa annullata: hai scelto un altro dispositivo.");
            }

            ApplyAudioOutputDevice(value);
            SyncChosenDevice(value);
        }
    }

    /// <summary>
    /// Dispositivo scelto dall'utente nei menu: accetta anche le uscite non attive
    /// e avvia il tentativo di collegamento invece di respingerle.
    /// </summary>
    public AudioOutputDevice? ChosenAudioOutputDevice
    {
        get => _chosenAudioOutputDevice;
        set
        {
            if (_suppressChosenApply || ReferenceEquals(_chosenAudioOutputDevice, value)) return;
            _chosenAudioOutputDevice = value;
            OnPropertyChanged(nameof(ChosenAudioOutputDevice));
            if (value is not null) _ = ConnectAudioDeviceAsync(value);
        }
    }

    private void SyncChosenDevice(AudioOutputDevice? device)
    {
        if (ReferenceEquals(_chosenAudioOutputDevice, device)) return;
        _suppressChosenApply = true;
        _chosenAudioOutputDevice = device;
        OnPropertyChanged(nameof(ChosenAudioOutputDevice));
        _suppressChosenApply = false;
    }

    /// <summary>Etichetta coerente con il dispositivo di uscita selezionato.</summary>
    public string VolumeLabel => SelectedAudioOutputDevice is { VolumeAvailable: true, IsDefault: false }
        ? "Volume dispositivo selezionato"
        : "Volume Windows";

    /// <summary>Stato del dispositivo che l'utente ha chiesto di collegare.</summary>
    public string PendingDeviceMessage => string.IsNullOrWhiteSpace(Settings.PendingAudioDeviceName)
        ? "Nessuna uscita in attesa: scegli un dispositivo per usarlo come voce."
        : $"In attesa di '{Settings.PendingAudioDeviceName}': appena diventa attivo verrà selezionato automaticamente.";

    /// <summary>Attiva/disattiva la lettura vocale dei sottotitoli.</summary>
    public bool VoiceEnabled
    {
        get => Settings.VoiceEnabled;
        set
        {
            if (Settings.VoiceEnabled == value) return;
            Settings.VoiceEnabled = value;
            OnPropertyChanged(nameof(VoiceEnabled));
            OnPropertyChanged(nameof(VoiceToggleLabel));
            _settingsService.Save(Settings);
            if (!value) _speech.Stop();
            EvaluateVoiceStatus();
            AppendLog(value ? "Lettura vocale attivata." : "Lettura vocale disattivata.");
        }
    }

    /// <summary>Etichetta del pulsante di modalità voce/sottotitoli.</summary>
    public string VoiceToggleLabel => VoiceEnabled ? "VOCE ATTIVA" : "SOLO SOTTOTITOLI";

    /// <summary>Stato di muto dell'uscita audio selezionata.</summary>
    public bool SystemMuted
    {
        get => _systemMuted;
        private set => SetField(ref _systemMuted, value);
    }

    /// <summary>Vero quando il pannello di controllo rapido è visibile.</summary>
    public bool QuickControlVisible => _quickControl is { IsVisible: true };

    /// <summary>Mostra o nasconde le uscite non attive.</summary>
    public void ToggleActiveAudioDevices(bool onlyActive)
    {
        Settings.ShowOnlyActiveAudioDevices = onlyActive;
        OnPropertyChanged(nameof(VisibleAudioOutputDevices));
        _settingsService.Save(Settings);

        var selected = SelectedAudioOutputDevice;
        if (onlyActive && selected is { IsActive: false })
        {
            var fallback = AudioOutputDevices.FirstOrDefault(d => d.IsDefault)
                           ?? AudioOutputDevices.FirstOrDefault(d => d.IsActive);
            _suppressAudioDeviceApply = true;
            SelectedAudioOutputDevice = fallback;
            _suppressAudioDeviceApply = false;
            if (fallback is not null) ApplyAudioOutputDevice(fallback);
        }
    }

    /// <summary>Volume dell'uscita audio selezionata (0-100).</summary>
    public int SystemVolume
    {
        get => _systemVolume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (!SetField(ref _systemVolume, clamped)) return;

            try
            {
                var selected = SelectedAudioOutputDevice;
                _systemVolumeController.SetVolume(clamped, selected is { VolumeAvailable: true } ? selected.Id : null);
                if (clamped > 0) SystemMuted = false;
                Settings.SystemVolume = clamped;
                _settingsService.Save(Settings);
                AppendLog($"{VolumeLabel} impostato: {clamped}%.");
            }
            catch (Exception ex)
            {
                AppendLog($"Impossibile impostare il volume audio: {ex.Message}");
            }
        }
    }

    private async Task RefreshAudioDevicesAsync()
    {
        IReadOnlyList<AudioOutputDevice> devices;
        try
        {
            devices = await AudioOutputDeviceService.GetDevicesAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"Dispositivi audio non disponibili: {ex.Message}");
            return;
        }

        Dispatch(() =>
        {
            AudioOutputDevices.Clear();
            foreach (var device in devices) AudioOutputDevices.Add(device);
            OnPropertyChanged(nameof(VisibleAudioOutputDevices));

            var pendingName = Settings.PendingAudioDeviceName;
            var pendingDevice = string.IsNullOrWhiteSpace(pendingName)
                ? null
                : devices.FirstOrDefault(d => d.IsActive && DeviceNameMatches(d.Name, pendingName!));

            if (pendingDevice is not null)
            {
                SetPendingDevice(null);
                StopPendingDeviceWatch();
            }

            var saved = Settings.AudioOutputDeviceId;
            var savedDevice = saved is null ? null : devices.FirstOrDefault(d =>
                string.Equals(d.Id, saved, StringComparison.OrdinalIgnoreCase));
            // Preferenza: dispositivo appena collegato, poi quello salvato, poi le cuffie M8,
            // poi qualunque uscita con nome "Cuffie", poi un'uscita reale non virtuale.
            var selected = pendingDevice
                           ?? (savedDevice is { IsActive: true } ? savedDevice : null)
                           ?? devices.FirstOrDefault(d => d.IsActive &&
                                   d.Name.Contains("M8", StringComparison.OrdinalIgnoreCase))
                           ?? devices.FirstOrDefault(d => d.IsActive &&
                                   d.Name.Contains("Cuffie", StringComparison.OrdinalIgnoreCase))
                           ?? devices.FirstOrDefault(d => d.IsActive &&
                                   !d.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                           ?? devices.FirstOrDefault(d => d.IsDefault)
                           ?? devices.FirstOrDefault(d => d.IsActive)
                           ?? devices.FirstOrDefault();

            _suppressAudioDeviceApply = true;
            SelectedAudioOutputDevice = selected;
            _suppressAudioDeviceApply = false;

            if (selected is not null) ApplyAudioOutputDevice(selected);
            if (string.IsNullOrWhiteSpace(Settings.PendingAudioDeviceName))
                SyncChosenDevice(selected);
            if (pendingDevice is not null)
                AppendLog($"Dispositivo atteso '{pendingDevice.Name}' ora attivo: selezionato automaticamente.");
            AppendLog($"Uscite audio rilevate: {devices.Count} ({devices.Count(d => d.IsActive)} attive).");
        });
    }

    /// <summary>
    /// Tenta di collegare/abilitare l'uscita scelta. Se Windows non può farlo subito,
    /// apre le impostazioni audio e seleziona automaticamente il dispositivo appena attivo.
    /// </summary>
    public async Task<bool> ConnectAudioDeviceAsync(AudioOutputDevice device)
    {
        if (device.IsActive)
        {
            ClearPendingDevice();
            SelectedAudioOutputDevice = device;
            SyncChosenDevice(device);
            return true;
        }

        AppendLog($"Tentativo di collegare l'uscita audio: {device.Name} ({device.Status}).");
        var result = await AudioDeviceActivationService.ActivateAsync(device);

        if (result.Activated)
        {
            await RefreshAudioDevicesAsync();
            var active = AudioOutputDevices.FirstOrDefault(d => d.IsActive &&
                (string.Equals(d.Id, device.Id, StringComparison.OrdinalIgnoreCase) ||
                 DeviceNameMatches(d.Name, device.Name)));
            if (active is not null)
            {
                SelectedAudioOutputDevice = active;
                SetPendingDevice(null);
                StopPendingDeviceWatch();
                SyncChosenDevice(active);
                AppendLog(result.Message);
                AppendLog($"Uscita '{active.Name}' pronta per la lettura vocale.");
                return true;
            }
        }

        SetPendingDevice(device.Name);
        Settings.AudioOutputDeviceId = device.Id;
        _settingsService.Save(Settings);
        StartPendingDeviceWatch();
        OpenWindowsAudioSettings();
        AppendLog($"{result.Message} Ho aperto 'Impostazioni audio' di Windows: quando l'uscita sarà attiva la selezionerò da sola.");
        return false;
    }

    /// <summary>Annulla l'uscita lasciata in attesa e ferma il controllo automatico.</summary>
    public void ClearPendingDevice()
    {
        SetPendingDevice(null);
        StopPendingDeviceWatch();
    }

    private void SetPendingDevice(string? name)
    {
        if (string.Equals(Settings.PendingAudioDeviceName, name, StringComparison.OrdinalIgnoreCase)) return;
        Settings.PendingAudioDeviceName = name;
        OnPropertyChanged(nameof(PendingDeviceMessage));
        _settingsService.Save(Settings);
    }

    private static bool DeviceNameMatches(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
           (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
            (left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
             right.Contains(left, StringComparison.OrdinalIgnoreCase)));

    private void StartPendingDeviceWatch()
    {
        _pendingDeviceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pendingDeviceTimer.Tick -= PendingDeviceTimer_Tick;
        _pendingDeviceTimer.Tick += PendingDeviceTimer_Tick;
        if (!_pendingDeviceTimer.IsEnabled) _pendingDeviceTimer.Start();
    }

    private void StopPendingDeviceWatch() => _pendingDeviceTimer?.Stop();

    private async void PendingDeviceTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            await RefreshAudioDevicesAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"Controllo del dispositivo in attesa non riuscito: {ex.Message}");
        }
    }

    private void OpenWindowsAudioSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,1") { UseShellExecute = true });
            }
            catch
            {
                AppendLog("Impossibile aprire le impostazioni audio di Windows: apri 'Impostazioni > Sistema > Audio' manualmente.");
            }
        }
    }

    private void ApplyAudioOutputDevice(AudioOutputDevice? device)
    {
        Settings.AudioOutputDeviceId = device?.Id;

        var voiceUsable = device is { IsActive: true, Device: not null };
        var volumeUsable = device is null || device.VolumeAvailable;
        _speech.SetOutputDevice(voiceUsable ? device!.Device : null);

        try
        {
            _systemVolume = device is { VolumeAvailable: true }
                ? _systemVolumeController.GetVolume(device.Id)
                : _systemVolumeController.GetVolume();
            Settings.SystemVolume = _systemVolume;
            SystemMuted = _systemVolumeController.GetMute(device is { VolumeAvailable: true } ? device.Id : null);
            OnPropertyChanged(nameof(SystemVolume));
        }
        catch (Exception ex)
        {
            AppendLog($"Volume del dispositivo audio non disponibile: {ex.Message}");
        }

        OnPropertyChanged(nameof(VolumeLabel));
        _settingsService.Save(Settings);

        AppendLog(device is null
            ? "Dispositivo audio di uscita: predefinito di Windows."
            : $"Dispositivo audio di uscita: {device.Name} ({device.Status}).");

        if (device is { IsActive: false })
            AppendLog($"Dispositivo non selezionabile: {device.Name} ({device.Status}). {device.AvailabilityHint}");

        EvaluateVoiceStatus();
    }

    private void RefreshDevices()
    {
        Monitors.Clear();
        foreach (var monitor in Win32DisplayEnumerator.GetMonitors()) Monitors.Add(monitor);
        Windows.Clear();
        foreach (var window in Win32DisplayEnumerator.GetWindows()) Windows.Add(window);

        if (Settings.SelectedMonitorIndex >= Monitors.Count)
            Settings.SelectedMonitorIndex = 0;

        AppendLog($"Dispositivi aggiornati: {Monitors.Count} monitor, {Windows.Count} finestre.");
    }


    /// <summary>
    /// Ricarica periodicamente l'elenco reale delle lingue. Il servizio locale può
    /// essere avviato in ritardo rispetto all'app: senza questo aggiornamento le
    /// destinazioni resterebbero segnate come "da installare" anche dopo l'avvio.
    /// </summary>
    private void StartLanguageRefreshTimer()
    {
        _languageRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _languageRefreshTimer.Tick -= LanguageRefreshTimer_Tick;
        _languageRefreshTimer.Tick += LanguageRefreshTimer_Tick;
        if (!_languageRefreshTimer.IsEnabled) _languageRefreshTimer.Start();
    }

    private void LanguageRefreshTimer_Tick(object? sender, EventArgs e)
        => _ = LoadLanguageOptionsAsync();

    private async Task RefreshLanguagesAsync()
    {
        _autoStartAttempted = false;
        await LoadLanguageOptionsAsync();
        AppendLog("Elenco lingue e modelli locali aggiornato.");
    }

    /// <summary>
    /// Avvia LibreTranslate in background se è installato ma non risponde.
    /// L'utente non deve più lanciare a mano AvviaLibreTranslate.cmd.
    /// </summary>
    private async Task<bool> EnsureLocalTranslationServiceAsync()
    {
        var baseUrl = string.IsNullOrWhiteSpace(Settings.LocalTranslateBaseUrl)
            ? "http://127.0.0.1:5000"
            : Settings.LocalTranslateBaseUrl.TrimEnd('/');

        var probe = await LanguageCatalog.QueryAsync(baseUrl, 1500).ConfigureAwait(false);
        if (probe.TranslationServiceReachable)
        {
            _autoStartAttempted = false;
            return true;
        }

        if (_autoStartAttempted) return false;
        _autoStartAttempted = true;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var exe = Path.Combine(localAppData, "GameTranslatorIT", "LibreTranslate", "Scripts", "libretranslate.exe");
        if (!File.Exists(exe)) return false;

        try
        {
            var start = new ProcessStartInfo(exe, "--host 127.0.0.1 --port 5000 --disable-web-ui")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? localAppData
            };
            start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            var process = Process.Start(start);
            AppendLog(process is null
                ? "Avvio automatico del servizio di traduzione locale non riuscito."
                : "Servizio di traduzione locale avviato automaticamente in background.");
        }
        catch (Exception ex)
        {
            AppendLog($"Avvio automatico del servizio locale non riuscito: {ex.Message}");
            return false;
        }

        // Il primo avvio caricamento dei modelli può richiedere qualche secondo.
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            var check = await LanguageCatalog.QueryAsync(baseUrl, 1000).ConfigureAwait(false);
            if (check.TranslationServiceReachable) return true;
        }

        AppendLog("Servizio locale non ancora pronto: le destinazioni saranno aggiornate automaticamente.");
        return false;
    }

    private async Task LoadLanguageOptionsAsync()
    {
        if (_languageRefreshRunning || _disposed) return;
        _languageRefreshRunning = true;
        try
        {
            await EnsureLocalTranslationServiceAsync().ConfigureAwait(false);

            var ocrTags = LanguageCatalog.GetOcrLanguageTags();
            var available = await LanguageCatalog.QueryAsync(
                Settings.LocalTranslateBaseUrl, Settings.TranslationTimeoutMs).ConfigureAwait(false);

            var previous = _lastAvailableLanguages;
            var changed = previous is null
                || previous.TranslationServiceReachable != available.TranslationServiceReachable
                || !previous.SourceCodes.SetEquals(available.SourceCodes)
                || !previous.TargetCodes.SetEquals(available.TargetCodes);

            _lastAvailableLanguages = available;
            (_provider as CompositeTranslationProvider)?.UpdateLocalTargets(available.TargetCodes);

            var allowOnline = Settings.AllowOnlineFallbackForMissingLanguages;
            var currentSource = Settings.SourceLanguage;
            var currentTarget = Settings.TargetLanguage;

            var onlineNote = allowOnline
                ? "Le 8 lingue senza modello usano il fallback online (testo inviato online)."
                : "Le 8 lingue senza modello richiedono l'opzione online nelle impostazioni.";
            var hint = available.TranslationServiceReachable
                ? $"{LanguageCatalog.TotalCount} lingue selezionabili · {available.TargetCodes.Count} destinazioni locali pronte. {onlineNote}"
                : $"{LanguageCatalog.TotalCount} lingue selezionabili · servizio locale non raggiungibile, avvio automatico in corso. {onlineNote}";

            if (changed)
            {
                var sources = LanguageCatalog.BuildSourceOptions(ocrTags, available, currentSource, allowOnline);
                var targets = LanguageCatalog.BuildTargetOptions(ocrTags, available, currentTarget, allowOnline);
                IsRefreshingLanguageOptions = true;
                Dispatch(() =>
                {
                    SourceLanguageOptions = new ObservableCollection<LanguageOption>(sources);
                    TargetLanguageOptions = new ObservableCollection<LanguageOption>(targets);

                    // La sostituzione della collection non tocca il binding SelectedValue,
                    // ma reimpostiamo i valori per sicurezza (una selezione sopravvissuta
                    // a un Clear può essere stata riportata al primo elemento).
                    Settings.SourceLanguage = currentSource;
                    Settings.TargetLanguage = currentTarget;
                    LanguageHint = hint;
                    IsRefreshingLanguageOptions = false;
                });
                AppendLog(available.TranslationServiceReachable
                    ? $"Modelli locali aggiornati: {available.TargetCodes.Count} destinazioni pronte."
                    : "Servizio locale non raggiungibile: le lingue restano selezionabili con avvisi sui modelli.");
            }
            else
            {
                Dispatch(() => LanguageHint = hint);
            }
        }
        finally
        {
            _languageRefreshRunning = false;
        }
    }

    /// <summary>True mentre ricarichiamo le combo: evita di risalvare la selezione vecchia.</summary>
    public bool IsRefreshingLanguageOptions
    {
        get => _isRefreshingLanguageOptions;
        private set
        {
            if (_isRefreshingLanguageOptions == value) return;
            _isRefreshingLanguageOptions = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Salva le lingue scelte nella colonna sinistra e le applica senza riavviare
    /// l'applicazione né interrompere la cattura dei sottotitoli.
    /// </summary>
    public void SaveLanguageSettings()
    {
        if (IsRefreshingLanguageOptions) return;
        _settingsService.Save(Settings);
        _pipeline?.ApplyLanguageSettings();
        // Le opzioni non dipendono dalla lingua selezionata: non ricaricarle qui,
        // altrimenti il ripristino della SelectedValue può risembrare SelectionChanged.
        AppendLog("Lingue aggiornate in tempo reale (nessun riavvio dell'app).");
    }

    /// <summary>Scegliere un monitor attiva subito la cattura a schermo intero.</summary>
    public void SelectMonitor()
    {
        if (Settings.CaptureMode == CaptureMode.FullScreen) return;
        Settings.CaptureMode = CaptureMode.FullScreen;
        SaveAndRestartIfRunning("Cattura su schermo intero attivata dalla scelta del monitor.");
    }

    /// <summary>Scegliere una finestra attiva subito la cattura della finestra di gioco.</summary>
    public void SelectGameWindow()
    {
        if (Settings.CaptureMode == CaptureMode.GameWindow) return;
        Settings.CaptureMode = CaptureMode.GameWindow;
        SaveAndRestartIfRunning("Cattura della finestra di gioco attivata dalla scelta della finestra.");
    }

    /// <summary>Salva volume e velocità della voce senza interrompere la frase in corso.</summary>
    public void SaveVoiceSettings()
    {
        Settings.VoiceVolume = Math.Clamp(Settings.VoiceVolume, 0, 100);
        Settings.VoiceRate = Math.Clamp(Settings.VoiceRate, 50, 200);
        _settingsService.Save(Settings);
    }

    /// <summary>Salva le impostazioni modificate dal pannello di controllo.</summary>
    public void SaveSettings() => _settingsService.Save(Settings);

    /// <summary>Applica le impostazioni di cattura/OCR modificate dal pannello.</summary>
    public void ApplyCaptureSettings()
    {
        SaveAndRestartIfRunning("Impostazioni di cattura e OCR aggiornate.");
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Applica le impostazioni del servizio di traduzione modificate dal pannello.</summary>
    public void ApplyTranslationSettings()
    {
        _settingsService.Save(Settings);
        _provider = CreateProvider();
        if (IsRunning)
        {
            Stop();
            Start();
        }
        AppendLog("Impostazioni di traduzione aggiornate.");
    }

    private void SaveAndRestartIfRunning(string message)
    {
        AppendLog(message);
        _settingsService.Save(Settings);
        if (!IsRunning) return;
        Stop();
        Start();
    }

    private CaptureRegionConfig? ResolveRegion(object? parameter) => parameter switch
    {
        CaptureRegionConfig config => config,
        int index => GetRegion(index),
        string id => Settings.CaptureRegions.FirstOrDefault(r =>
            string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.Name, id, StringComparison.OrdinalIgnoreCase)),
        _ => null
    };

    private CaptureRegionConfig? GetRegion(int index)
        => index >= 0 && index < Settings.CaptureRegions.Count ? Settings.CaptureRegions[index] : null;

    private static string ModeLabel(RegionMuteMode mode) => mode switch
    {
        RegionMuteMode.SoloVoce => "solo voce",
        RegionMuteMode.Blocca => "blocca",
        RegionMuteMode.Accoda => "accoda",
        _ => "solo voce"
    };

    /// <summary>Inizializza i tasti rapidi quando la finestra principale ha un handle.</summary>
    public void InitializeHotkeys(IntPtr hwnd)
    {
        _hotkeyHwnd = hwnd;
        RefreshHotkeys();
        if (Settings.QuickControlAlwaysVisible) Dispatch(ShowQuickControl);
    }

    private void RefreshHotkeys()
    {
        _hotkeys?.Dispose();
        _hotkeys = null;

        if (!Settings.HotkeysEnabled || _hotkeyHwnd == IntPtr.Zero) return;

        _hotkeys = new GlobalHotkeyService();
        var attached = _hotkeys.Attach(
            _hotkeyHwnd,
            index => ToggleRegionMute(GetRegion(index)),
            index => SelectRegion(GetRegion(index)),
            index => CycleRegionMode(GetRegion(index)),
            index => RemoveRegion(GetRegion(index)),
            ToggleQuickControl,
            OpenControlPanel);

        AppendLog(attached
            ? $"Tasti rapidi attivi: {_hotkeys.RegisteredCount} combinazioni (F1–F8, Ctrl+Shift+P/D)."
            : "Tasti rapidi non registrati: le combinazioni sono occupate da altre applicazioni.");
    }

    /// <summary>Apre (o porta in primo piano) il pannello di controllo completo.</summary>
    public void OpenControlPanel()
    {
        Dispatch(() =>
        {
            if (_controlPanel is null)
            {
                var window = new ControlPanelWindow(this);
                window.SourceInitialized += (_, _) => WindowCaptureExclusion.Exclude(window);
                window.Closed += (_, _) =>
                {
                    WindowCaptureExclusion.Restore(window);
                    _controlPanel = null;
                };
                _controlPanel = window;
            }

            _controlPanel.Show();
            if (_controlPanel.WindowState == WindowState.Minimized)
                _controlPanel.WindowState = WindowState.Normal;
            _controlPanel.Activate();
        });
    }

    /// <summary>Apre la finestra dedicata alla scelta del dispositivo audio di uscita.</summary>
    public void OpenAudioDeviceWindow()
    {
        Dispatch(() =>
        {
            var window = new AudioDeviceWindow(this) { Owner = Application.Current.MainWindow };
            window.SourceInitialized += (_, _) => WindowCaptureExclusion.Exclude(window);
            window.Closed += (_, _) => WindowCaptureExclusion.Restore(window);
            window.ShowDialog();
        });
    }

    /// <summary>Apre la finestra per scegliere posizione e dimensioni dell'overlay delle traduzioni.</summary>
    public void OpenOverlayGeometryWindow()
    {
        Dispatch(() =>
        {
            var window = new OverlayGeometryWindow(this) { Owner = Application.Current.MainWindow };
            window.SourceInitialized += (_, _) => WindowCaptureExclusion.Exclude(window);
            window.Closed += (_, _) => WindowCaptureExclusion.Restore(window);
            window.ShowDialog();
        });
    }

    /// <summary>Applica la geometria personalizzata dell'overlay e la salva.</summary>
    public void SetOverlayGeometry(double left, double top, double width, double height)
    {
        _overlay.ApplyCustomGeometry(left, top, width, height);
        _settingsService.Save(Settings);
        AppendLog($"Overlay personalizzato: X={left:0}, Y={top:0}, larghezza={width:0}, altezza={height:0}.");
    }

    /// <summary>Mostra o nasconde il pannello di controllo rapido in-game.</summary>
    public void ToggleQuickControl()
    {
        Dispatch(() =>
        {
            if (_quickControl is { IsVisible: true }) HideQuickControl();
            else ShowQuickControl();
        });
    }

    private void ShowQuickControl()
    {
        if (_quickControl is null)
        {
            var window = new QuickControlWindow(this);
            window.SourceInitialized += (_, _) => WindowCaptureExclusion.Exclude(window);
            window.Closed += (_, _) =>
            {
                WindowCaptureExclusion.Restore(window);
                _quickControl = null;
                OnPropertyChanged(nameof(QuickControlVisible));
            };
            _quickControl = window;
        }

        _quickControl.Show();
        _quickControl.Activate();
        OnPropertyChanged(nameof(QuickControlVisible));
    }

    public void HideQuickControl()
    {
        if (_quickControl is not { IsVisible: true }) return;
        _quickControl.Hide();
        OnPropertyChanged(nameof(QuickControlVisible));
    }

    /// <summary>Pronuncia una frase di prova sull'uscita audio selezionata.</summary>
    private async Task TestVoiceAsync()
    {
        if (!Settings.VoiceEnabled)
        {
            AppendLog("Prova voce non eseguita: attiva prima 'Leggi i sottotitoli a voce'.");
            return;
        }

        var deviceName = SelectedAudioOutputDevice?.Name ?? "predefinito di Windows";
        try
        {
            await _speech.SpeakAsync("Prova della voce italiana.", Settings.VoiceVolume, Settings.VoiceRate);
            AppendLog($"Prova voce eseguita su: {deviceName}.");
        }
        catch (Exception ex)
        {
            AppendLog($"Prova voce non riuscita su {deviceName}: {ex.Message}");
        }
    }

    private void ToggleSystemMute()
    {
        try
        {
            var device = SelectedAudioOutputDevice;
            var muted = !SystemMuted;
            _systemVolumeController.SetMute(muted, device is { VolumeAvailable: true } ? device.Id : null);
            SystemMuted = muted;
            AppendLog(muted ? "Muto audio di sistema attivato." : "Muto audio di sistema disattivato.");
        }
        catch (Exception ex)
        {
            AppendLog($"Impossibile cambiare il muto audio: {ex.Message}");
        }
    }

    private void ToggleRegions() => UseCaptureRegions = !UseCaptureRegions;

    public void SelectRegion(CaptureRegionConfig? region)
    {
        if (region is null) return;
        var index = Settings.CaptureRegions.IndexOf(region);
        if (index < 0) return;

        var area = ResolveArea();
        if (area.Width <= 0 || area.Height <= 0)
        {
            AppendLog("Selezione riquadro impossibile: area di cattura non valida.");
            return;
        }

        Dispatch(() =>
        {
            var selector = new RegionSelectorWindow(area, Settings.CaptureRegions, index)
            {
                Owner = Application.Current.MainWindow
            };
            if (selector.ShowDialog() != true) return;

            Settings.UseCaptureRegions = true;
            OnPropertyChanged(nameof(UseCaptureRegions));
            OnPropertyChanged(nameof(RegionsSummary));
            SaveAndRestartIfRunning($"Riquadro '{region.Name}' selezionato: {region.Coordinates}.");
        });
    }

    public void ToggleRegionMute(CaptureRegionConfig? region)
    {
        if (region is null) return;

        region.Muted = !region.Muted;
        _settingsService.Save(Settings);
        OnPropertyChanged(nameof(RegionsSummary));
        _overlay.UpdateRegionState(region.Id, region.Name, region.Muted, ModeLabel(region.MuteMode));

        if (region.Muted)
        {
            if (string.Equals(_speakingRegionId, region.Id, StringComparison.OrdinalIgnoreCase))
            {
                _speech.Stop();
                _speakingRegionId = null;
            }
            AppendLog($"Riquadro '{region.Name}' silenziato ({ModeLabel(region.MuteMode)}).");
            return;
        }

        AppendLog($"Riquadro '{region.Name}' riattivato.");
        if (region.MuteMode == RegionMuteMode.Accoda &&
            _pendingSpeech.TryGetValue(region.Id, out var queue) && queue.Count > 0)
        {
            _ = SpeakQueueAsync(region.Id);
        }
    }

    public void SetRegionMute(CaptureRegionConfig? region, bool muted)
    {
        if (region is null || region.Muted == muted) return;
        ToggleRegionMute(region);
    }

    public void SetRegionEnabled(CaptureRegionConfig? region, bool enabled)
    {
        if (region is null || region.Enabled == enabled) return;
        region.Enabled = enabled;
        OnPropertyChanged(nameof(RegionsSummary));
        SaveAndRestartIfRunning($"Riquadro '{region.Name}' {(enabled ? "attivato" : "disattivato")}.");
    }

    public void RenameRegion(CaptureRegionConfig? region, string name)
    {
        if (region is null || string.IsNullOrWhiteSpace(name)) return;
        region.Name = name.Trim();
        _settingsService.Save(Settings);
        _overlay.UpdateRegionState(region.Id, region.Name, region.Muted, ModeLabel(region.MuteMode));
    }

    public void CycleRegionMode(CaptureRegionConfig? region)
    {
        if (region is null) return;
        var modes = Enum.GetValues<RegionMuteMode>();
        var current = Array.IndexOf(modes, region.MuteMode);
        region.MuteMode = modes[(current + 1) % modes.Length];
        _settingsService.Save(Settings);
        _overlay.UpdateRegionState(region.Id, region.Name, region.Muted, ModeLabel(region.MuteMode));
        AppendLog($"Riquadro '{region.Name}': modalità muto {ModeLabel(region.MuteMode)}.");
    }

    public void SetRegionMode(CaptureRegionConfig? region, RegionMuteMode mode)
    {
        if (region is null || region.MuteMode == mode) return;
        region.MuteMode = mode;
        _settingsService.Save(Settings);
        _overlay.UpdateRegionState(region.Id, region.Name, region.Muted, ModeLabel(mode));
        AppendLog($"Riquadro '{region.Name}': modalità muto {ModeLabel(mode)}.");
    }

    public void RemoveRegion(CaptureRegionConfig? region)
    {
        if (region is null) return;
        region.Enabled = false;
        region.Muted = false;
        _pendingSpeech.Remove(region.Id);
        _overlay.RemoveRegionEntry(region.Id);
        _settingsService.Save(Settings);
        OnPropertyChanged(nameof(RegionsSummary));
        AppendLog($"Riquadro '{region.Name}' rimosso dalla lettura.");
    }

    private void AddRegion()
    {
        if (Settings.CaptureRegions.Count >= 12)
        {
            AppendLog("Numero massimo di riquadri (12) raggiunto.");
            return;
        }

        Settings.RegionCount++;
        Settings.EnsureCaptureRegions();
        OnPropertyChanged(nameof(RegionCount));
        OnPropertyChanged(nameof(Regions));
        OnPropertyChanged(nameof(RegionsSummary));
        _settingsService.Save(Settings);
        SelectRegion(Settings.CaptureRegions[^1]);
    }

    private void QueueSpeech(string regionId, string text)
    {
        if (!_pendingSpeech.TryGetValue(regionId, out var queue))
        {
            queue = new Queue<string>();
            _pendingSpeech[regionId] = queue;
        }

        queue.Enqueue(text);
        while (queue.Count > 30) queue.Dequeue();
    }

    private async Task SpeakQueueAsync(string regionId)
    {
        if (!_pendingSpeech.TryGetValue(regionId, out var queue)) return;
        while (queue.Count > 0)
        {
            if (queue.TryDequeue(out var text))
                await SpeakSafeAsync(text, regionId);
        }
    }

    private void ToggleVoice() => VoiceEnabled = !VoiceEnabled;

    private void EvaluateVoiceStatus()
    {
        if (!Settings.VoiceEnabled)
        {
            VoiceStatus = new ComponentStatus(ComponentState.Disabilitato, "Disattivata");
            return;
        }

        VoiceStatus = _speech.VoiceName is not null
            ? new ComponentStatus(ComponentState.Pronto, $"Voce: {_speech.VoiceName}")
            : new ComponentStatus(ComponentState.Avviso, "Nessuna voce italiana installata in Windows");
    }

    private async Task SpeakSafeAsync(string text, string? regionId = null)
    {
        _speakingRegionId = regionId;
        try
        {
            await _speech.SpeakAsync(text, Settings.VoiceVolume, Settings.VoiceRate);
        }
        catch (OperationCanceledException)
        {
            // Una nuova frase ha sostituito la lettura precedente.
        }
        catch (Exception ex)
        {
            Dispatch(() =>
            {
                VoiceStatus = new ComponentStatus(ComponentState.Errore, ex.Message);
                AppendLog($"Voce: {ex.Message}");
            });
        }
        finally
        {
            if (string.Equals(_speakingRegionId, regionId, StringComparison.OrdinalIgnoreCase))
                _speakingRegionId = null;
        }
    }

    private void Start()
    {
        if (_pipeline is { IsRunning: true }) return;

        ScreenRect area = ResolveArea();
        if (area.Width <= 0 || area.Height <= 0)
        {
            CaptureStatus = new ComponentStatus(ComponentState.Errore, "Area di cattura non valida");
            StatusText = "Errore: seleziona una sorgente valida";
            return;
        }

        _provider = CreateProvider();
        _pipeline = new TranslationPipeline(Settings, _provider);
        HookPipeline(_pipeline);

        var (overlayArea, showOverlay, forceVoice) = ResolveOverlayFallback(area);
        _forceVoice = forceVoice;
        _overlay.SetTargetArea(overlayArea);
        _overlay.ApplySettings();
        if (showOverlay) _overlay.Show(); else _overlay.Hide();

        // Evita che OCR legga l'interfaccia dell'app o l'overlay quando la cattura è l'intero monitor.
        WindowCaptureExclusion.Exclude(Application.Current.MainWindow);
        WindowCaptureExclusion.Exclude(_overlay);
        if (_controlPanel is not null) WindowCaptureExclusion.Exclude(_controlPanel);
        if (_quickControl is not null) WindowCaptureExclusion.Exclude(_quickControl);

        SubtitleCount = 0;
        TranslationCount = 0;
        LastSourceText = "—";
        LastTranslatedText = "—";
        _overlay.ClearTranslations();
        _pendingSpeech.Clear();
        _speakingRegionId = null;

        _pipeline.Start(Monitors.ToList(), Windows.ToList());
        IsRunning = true;
        StatusText = "Traduzione avviata";
        AppendLog(Settings.UseCaptureRegions
            ? $"Pipeline avviata su {Settings.CaptureRegions.Count(r => r.Enabled)} riquadri attivi."
            : "Pipeline avviata: cattura reale → OCR di Windows → traduzione locale → overlay.");
    }

    private void Stop()
    {
        WindowCaptureExclusion.Restore(Application.Current.MainWindow);
        WindowCaptureExclusion.Restore(_overlay);
        if (_controlPanel is not null) WindowCaptureExclusion.Restore(_controlPanel);
        if (_quickControl is not null) WindowCaptureExclusion.Restore(_quickControl);

        _pipeline?.Stop();
        _pipeline = null;
        _speech.Stop();
        _speakingRegionId = null;
        _overlay.ClearTranslations();
        _overlay.Hide();
        _pendingSpeech.Clear();

        IsRunning = false;
        StatusText = "Interrotto";
        AppendLog("Traduzione interrotta.");
    }

    private ScreenRect ResolveArea()
    {
        if (Settings.CaptureMode == CaptureMode.FullScreen && Monitors.Count > 0)
            return Monitors[Math.Clamp(Settings.SelectedMonitorIndex, 0, Monitors.Count - 1)].Bounds;

        var window = Windows.FirstOrDefault(w =>
            string.Equals(w.Title, Settings.SelectedWindowTitle, StringComparison.OrdinalIgnoreCase));
        if (window is not null) return window.Bounds;

        // La finestra salvata può non esistere più: usa il monitor principale
        // invece di bloccare selezione e pipeline.
        return Monitors.Count > 0 ? Monitors[0].Bounds : default;
    }

    private (ScreenRect Area, bool ShowOverlay, bool ForceVoice) ResolveOverlayFallback(ScreenRect sourceArea)
    {
        switch (Settings.OverlayFallback)
        {
            case OverlayFallback.SoloVoce:
                AppendLog("Fallback attivo: overlay disattivato, traduzione solo vocale.");
                return (sourceArea, false, true);

            case OverlayFallback.MonitorSecondario:
                var secondary = FindSecondaryMonitor();
                if (secondary is not null)
                {
                    AppendLog($"Fallback attivo: overlay spostato su {secondary.Name}.");
                    return (secondary.Bounds, true, false);
                }
                AppendLog("Monitor secondario non disponibile: overlay mantenuto sul monitor di gioco.");
                return (sourceArea, true, false);

            case OverlayFallback.SecondaryMonitorAndVoice:
                var secondaryVoice = FindSecondaryMonitor();
                if (secondaryVoice is not null)
                {
                    AppendLog($"Fallback attivo: overlay su {secondaryVoice.Name} + lettura vocale forzata.");
                    return (secondaryVoice.Bounds, true, true);
                }
                AppendLog("Monitor secondario non disponibile: lettura vocale forzata.");
                return (sourceArea, true, true);

            case OverlayFallback.SoloAvviso:
            default:
                return (sourceArea, true, false);
        }
    }

    private MonitorInfo? FindSecondaryMonitor()
        => Settings.CaptureMode == CaptureMode.FullScreen
            ? Monitors.FirstOrDefault(m => m.Index != Settings.SelectedMonitorIndex)
            : Monitors.FirstOrDefault(m => !m.IsPrimary);

    private void HookPipeline(TranslationPipeline pipeline)
    {
        pipeline.CaptureStatusChanged += (_, s) => Dispatch(() => CaptureStatus = s);
        pipeline.OcrStatusChanged += (_, s) => Dispatch(() => OcrStatus = s);
        pipeline.TranslationStatusChanged += (_, s) => Dispatch(() => TranslationStatus = s);
        pipeline.ErrorOccurred += (_, message) => Dispatch(() => AppendLog(message));

        pipeline.SubtitleRecognized += (_, args) => Dispatch(() =>
        {
            SubtitleCount++;
            LastSourceText = $"[{args.RegionName}] {args.Text}";
        });

        pipeline.Translated += (_, args) => Dispatch(() =>
        {
            TranslationCount++;
            LastTranslatedText = $"[{args.RegionName}] {args.Translated}";
            StatusText = "Traduzione attiva";

            var region = Settings.CaptureRegions.FirstOrDefault(r =>
                string.Equals(r.Id, args.RegionId, StringComparison.OrdinalIgnoreCase));
            var muted = region?.Muted ?? false;
            var mode = region?.MuteMode ?? RegionMuteMode.SoloVoce;
            var name = string.IsNullOrWhiteSpace(args.RegionName)
                ? region?.Name ?? "Riquadro"
                : args.RegionName;

            _overlay.ShowTranslation(
                args.Source,
                args.Translated,
                Settings.ShowOriginalAndTranslation,
                args.RegionId,
                name,
                muted,
                ModeLabel(mode));

            if (!Settings.VoiceEnabled && !_forceVoice) return;

            if (muted)
            {
                if (mode == RegionMuteMode.Accoda) QueueSpeech(args.RegionId, args.Translated);
                return;
            }

            _ = SpeakSafeAsync(args.Translated, args.RegionId);
        });
    }

    private void ToggleOverlayEdit()
    {
        _overlay.SetEditMode(!_overlay.IsEditMode);
        AppendLog(_overlay.IsEditMode
            ? "Overlay sbloccato: trascina il riquadro per spostarlo, usa l'angolo per ridimensionarlo."
            : "Overlay bloccato e trasparente ai click.");
    }

    private void OpenSettings()
    {
        AppendLog("Apertura impostazioni…");
        var draft = Settings.Clone();
        var pendingBefore = Settings.PendingAudioDeviceName;
        SettingsWindow window;
        try
        {
            window = new SettingsWindow(draft, ResolveArea, device => _ = ConnectAudioDeviceAsync(device))
            {
                Owner = Application.Current.MainWindow
            };
        }
        catch (Exception ex)
        {
            AppendLog($"Errore creazione finestra impostazioni: {ex}");
            return;
        }
        if (window.ShowDialog() != true)
        {
            AppendLog("Impostazioni chiuse senza salvataggio.");
            return;
        }

        Settings.CopyFrom(draft);
        Settings.PendingAudioDeviceName = pendingBefore;
        Settings.EnsureCaptureRegions();
        _settingsService.Save(Settings);
        _overlay.ApplySettings();
        EvaluateVoiceStatus();
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(RegionCount));
        OnPropertyChanged(nameof(Regions));
        OnPropertyChanged(nameof(RegionsSummary));
        RefreshHotkeys();
        if (Settings.CheckForUpdatesOnStartup) StartUpdateTimer();
        else { _updateTimer?.Dispose(); _updateTimer = null; }
        UpdateStatusText = $"Versione {CurrentVersion} — prossimo controllo tra {Math.Clamp(Settings.UpdateCheckIntervalHours, 1, 168)} ore.";

        _provider = CreateProvider();
        _pipeline?.UpdateProvider(_provider);
        _pipeline?.ApplyLanguageSettings();
        _ = LoadLanguageOptionsAsync();

        AppendLog("Impostazioni salvate e applicate senza riavvio.");
    }

    private ITranslationProvider CreateProvider()
    {
        var timeout = Math.Clamp(Settings.TranslationTimeoutMs, 500, 30000);
        var provider = new CompositeTranslationProvider(
            new LibreTranslateProvider(Settings.LocalTranslateBaseUrl, timeout),
            new OnlineTranslationProvider(Math.Max(8000, timeout)),
            () => Settings.AllowOnlineFallbackForMissingLanguages);
        if (_lastAvailableLanguages is not null)
            provider.UpdateLocalTargets(_lastAvailableLanguages.TargetCodes);
        return provider;
    }

    private async Task TestTranslationAsync()
    {
        TranslationStatus = new ComponentStatus(ComponentState.Attesa, "Verifica del servizio locale in corso…");
        try
        {
            var context = new TranslationContext(Settings.SourceLanguage, Settings.TargetLanguage,
                Array.Empty<string>(), Array.Empty<string>(), null);
            var result = await _provider.TranslateAsync("Hello world", context, CancellationToken.None);
            TranslationStatus = new ComponentStatus(ComponentState.Pronto,
                $"{result.ProviderName} — esito: {result.TranslatedText}");
            AppendLog($"Verifica traduzione riuscita: 'Hello world' → '{result.TranslatedText}' ({result.ProviderName}).");
        }
        catch (Exception ex)
        {
            TranslationStatus = new ComponentStatus(ComponentState.Errore, ex.Message);
            AppendLog($"Verifica traduzione non riuscita: {ex.Message}");
        }
    }

    private void StartUpdateTimer()
    {
        _updateTimer?.Dispose();
        var hours = Math.Clamp(Settings.UpdateCheckIntervalHours, 1, 168);
        var interval = TimeSpan.FromHours(hours);
        _updateTimer = new System.Threading.Timer(
            _ => { _ = CheckForUpdatesAsync(true, true); }, null, interval, interval);
    }

    public async Task CheckForUpdatesAsync(bool showWindowIfAvailable, bool silentErrors)
    {
        if (_disposed || _updateCheckRunning) return;
        _updateCheckRunning = true;

        try
        {
            UpdateStatus = new ComponentStatus(ComponentState.Attivo, "Verifica in corso");
            UpdateStatusText = "Verifica aggiornamenti in corso…";

            var result = await _updateService.CheckAsync().ConfigureAwait(false);
            if (!result.Success)
            {
                var message = result.ErrorMessage ?? "Errore sconosciuto durante la verifica.";
                UpdateStatus = new ComponentStatus(ComponentState.Errore, message);
                UpdateStatusText = "Verifica aggiornamenti non riuscita";
                AppendLog($"Controllo aggiornamenti non riuscito: {message}");
                if (!silentErrors)
                    AppendLog("L'app continua a funzionare normalmente. Controlla la connessione o la variabile GITHUB_TOKEN.");
                return;
            }

            Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settingsService.Save(Settings);

            var update = result.AvailableUpdate;
            if (update is null)
            {
                UpdateStatus = new ComponentStatus(ComponentState.Pronto, result.StatusMessage);
                UpdateStatusText = result.StatusMessage;
                AppendLog(result.StatusMessage);
                return;
            }

            UpdateStatus = new ComponentStatus(ComponentState.Avviso, $"Disponibile versione {update.Version}");
            UpdateStatusText = $"Nuova versione {update.Version} disponibile";
            AppendLog($"Nuova versione disponibile: {update.Version}. Asset: {update.Asset.Name}.");

            if (string.Equals(Settings.SkippedVersion, update.Version, StringComparison.OrdinalIgnoreCase))
            {
                AppendLog($"Versione {update.Version} ignorata su richiesta dell'utente.");
                return;
            }

            if (showWindowIfAvailable)
                Dispatch(() => ShowUpdateWindow(update));
        }
        catch (Exception ex)
        {
            UpdateStatus = new ComponentStatus(ComponentState.Errore, ex.Message);
            UpdateStatusText = "Verifica aggiornamenti non riuscita";
            AppendLog($"Controllo aggiornamenti interrotto: {ex.Message}");
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private void ShowUpdateWindow(UpdateInfo info)
    {
        if (_updateWindow is { IsLoaded: true })
        {
            _updateWindow.Activate();
            return;
        }

        var window = new UpdateWindow(info, this)
        {
            Owner = Application.Current.MainWindow
        };
        window.SourceInitialized += (_, _) => WindowCaptureExclusion.Exclude(window);
        window.Closed += (_, _) =>
        {
            WindowCaptureExclusion.Restore(window);
            if (ReferenceEquals(_updateWindow, window)) _updateWindow = null;
        };
        _updateWindow = window;
        window.Show();
    }

    public async Task ApplyUpdateAsync(UpdateInfo info, IProgress<string>? progress)
    {
        if (_updateApplyRunning) return;
        _updateApplyRunning = true;
        var reporting = progress ?? new Progress<string>(_ => { });

        try
        {
            UpdateStatus = new ComponentStatus(ComponentState.Attivo, "Download aggiornamento");
            UpdateStatusText = "Download e verifica dell'aggiornamento…";
            await _updateService.DownloadAndApplyAsync(info, reporting).ConfigureAwait(false);

            _settingsService.Save(Settings);
            AppendLog($"Aggiornamento {info.Version} scaricato e verificato. Riavvio in corso…");
            Dispatch(() => Application.Current.Shutdown());
        }
        catch (Exception ex)
        {
            UpdateStatus = new ComponentStatus(ComponentState.Errore, ex.Message);
            UpdateStatusText = "Aggiornamento non riuscito";
            AppendLog($"Aggiornamento non riuscito: {ex.Message}");
            throw;
        }
        finally
        {
            _updateApplyRunning = false;
        }
    }

    public void IgnoreUpdate(string version)
    {
        Settings.SkippedVersion = version;
        _settingsService.Save(Settings);
        AppendLog($"Aggiornamento {version} ignorato. Sarà riproposto alla versione successiva.");
        UpdateStatusText = $"Versione {version} ignorata";
        UpdateStatus = new ComponentStatus(ComponentState.Disabilitato, UpdateStatusText);
    }

    private void Dispatch(Action action)
    {
        var app = Application.Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Dispatch(() =>
        {
            LogEntries.Insert(0, line);
            while (LogEntries.Count > 80) LogEntries.RemoveAt(LogEntries.Count - 1);
        });
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        StopPendingDeviceWatch();
        _pendingDeviceTimer = null;
        _languageRefreshTimer?.Stop();
        _languageRefreshTimer = null;
        _updateTimer?.Dispose();
        _updateTimer = null;
        _updateWindow?.Close();
        _hotkeys?.Dispose();
        _controlPanel?.Close();
        _quickControl?.Close();
        _overlay.Close();
        _speech.Dispose();
        _disposed = true;
    }
}
