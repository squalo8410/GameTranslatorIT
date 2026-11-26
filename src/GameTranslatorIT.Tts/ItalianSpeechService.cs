using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Render;
using Windows.Media.SpeechSynthesis;

namespace GameTranslatorIT.Tts;

/// <summary>Voice installata in Windows, selezionabile dall'interfaccia.</summary>
public sealed record SpeechVoice(string Name, string Language)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Language) ? Name : $"{Name} ({Language})";
    public override string ToString() => DisplayName;
}

/// <summary>
/// Sintesi vocale reale tramite Windows.Media.SpeechSynthesis. L'audio viene
/// instradato sull'uscita selezionata, su tutte le uscite attive (se richiesto)
/// oppure su quella predefinita di Windows.
/// </summary>
public sealed class ItalianSpeechService : IDisposable
{
    private readonly List<AudioGraph> _graphs = new();
    private readonly List<AudioDeviceOutputNode> _outputNodes = new();
    private readonly List<MediaSourceAudioInputNode> _sourceNodes = new();
    private readonly List<MediaSource> _mediaSources = new();
    private DeviceInformation? _outputDevice;
    private bool _playOnAllDevices;
    private bool _disposed;

    /// <summary>Voce selezionata, se presente nel sistema.</summary>
    public string? VoiceName { get; private set; }

    /// <summary>Dispositivo di uscita usato quando non è attiva la riproduzione multipla.</summary>
    public string? OutputDeviceName => _outputDevice?.Name;

    /// <summary>Tutte le voci installate in Windows, non solo quelle italiane.</summary>
    public static IReadOnlyList<SpeechVoice> GetInstalledVoices()
    {
        try
        {
            return SpeechSynthesizer.AllVoices
                .Select(v => new SpeechVoice(v.DisplayName, v.Language))
                .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<SpeechVoice>();
        }
    }

    /// <summary>Imposta l'uscita per le prossime frasi.</summary>
    public void SetOutputDevice(DeviceInformation? device)
    {
        Stop();
        _outputDevice = device;
    }

    /// <summary>Abilita/disabilita la lettura simultanea su tutte le uscite attive.</summary>
    public void SetPlayOnAllDevices(bool enabled)
    {
        if (_playOnAllDevices == enabled) return;
        Stop();
        _playOnAllDevices = enabled;
    }

    /// <summary>Seleziona una voce installata; null o nome sconosciuto ripristina la prima italiana.</summary>
    public bool SetVoice(string? voiceName)
    {
        if (!string.IsNullOrWhiteSpace(voiceName))
        {
            var match = GetInstalledVoices().FirstOrDefault(v =>
                string.Equals(v.Name, voiceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                VoiceName = match.Name;
                return true;
            }
        }
        return Initialize();
    }

    /// <summary>Cerca una voce italiana installata in Windows.</summary>
    public bool Initialize()
    {
        try
        {
            var italianVoices = GetInstalledVoices()
                .Where(v => v.Language.StartsWith("it", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (italianVoices.Count == 0)
            {
                var first = GetInstalledVoices().FirstOrDefault();
                VoiceName = first?.Name;
                return first is not null;
            }

            VoiceName = italianVoices[0].Name;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Legge il testo a voce alta. Una nuova frase interrompe la precedente.</summary>
    public async Task SpeakAsync(string text, int volume, int rate)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ItalianSpeechService));
        if (string.IsNullOrWhiteSpace(text)) return;
        if (VoiceName is null && !Initialize())
            throw new InvalidOperationException("Nessuna voce installata in Windows.");

        var voice = GetInstalledVoices().FirstOrDefault(v =>
            string.Equals(v.Name, VoiceName, StringComparison.OrdinalIgnoreCase));
        var targets = await ResolveTargetsAsync().ConfigureAwait(false);

        Stop();
        var created = false;
        string? lastError = null;
        foreach (var target in targets)
        {
            using var synthesizer = new SpeechSynthesizer();
            if (voice is not null)
            {
                var installed = SpeechSynthesizer.AllVoices.FirstOrDefault(v =>
                    string.Equals(v.DisplayName, voice.Name, StringComparison.OrdinalIgnoreCase));
                if (installed is not null) synthesizer.Voice = installed;
            }
            synthesizer.Options.AudioVolume = Math.Clamp(volume / 100.0, 0, 1);
            synthesizer.Options.SpeakingRate = Math.Clamp(rate / 100.0, 0.5, 2.0);

            var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
            var settings = new AudioGraphSettings(AudioRenderCategory.Speech);
            if (target is not null) settings.PrimaryRenderDevice = target;

            var graphResult = await AudioGraph.CreateAsync(settings);
            if (graphResult.Status != AudioGraphCreationStatus.Success)
            {
                lastError = $"AudioGraph non disponibile: {graphResult.Status}";
                continue;
            }

            var graph = graphResult.Graph;
            var outputResult = await graph.CreateDeviceOutputNodeAsync();
            if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                lastError = $"Nodo audio di uscita non creato: {outputResult.Status}";
                graph.Dispose();
                continue;
            }

            var mediaSource = MediaSource.CreateFromStream(stream, "audio/wav");
            var inputResult = await graph.CreateMediaSourceAudioInputNodeAsync(mediaSource);
            if (inputResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
            {
                lastError = $"Sorgente audio non creata: {inputResult.Status}";
                mediaSource.Dispose();
                outputResult.DeviceOutputNode.Dispose();
                graph.Dispose();
                continue;
            }

            inputResult.Node.AddOutgoingConnection(outputResult.DeviceOutputNode);
            _graphs.Add(graph);
            _outputNodes.Add(outputResult.DeviceOutputNode);
            _mediaSources.Add(mediaSource);
            _sourceNodes.Add(inputResult.Node);
            created = true;
        }

        if (!created)
            throw new InvalidOperationException(lastError
                ?? "Nessun dispositivo di uscita audio disponibile per la voce.");

        foreach (var graph in _graphs) graph.Start();
    }

    private async Task<IReadOnlyList<DeviceInformation?>> ResolveTargetsAsync()
    {
        if (!_playOnAllDevices) return new DeviceInformation?[] { _outputDevice };

        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender);
            var active = devices.Where(d => d.IsEnabled).Cast<DeviceInformation?>().ToList();
            return active.Count > 0 ? active : new DeviceInformation?[] { _outputDevice };
        }
        catch
        {
            return new DeviceInformation?[] { _outputDevice };
        }
    }

    /// <summary>Interrompe subito la frase in corso.</summary>
    public void Stop()
    {
        foreach (var node in _sourceNodes) node.Dispose();
        foreach (var node in _outputNodes) node.Dispose();
        foreach (var source in _mediaSources) source.Dispose();
        foreach (var graph in _graphs) graph.Dispose();
        _sourceNodes.Clear();
        _outputNodes.Clear();
        _mediaSources.Clear();
        _graphs.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
