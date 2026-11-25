using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Render;
using Windows.Media.SpeechSynthesis;

namespace GameTranslatorIT.Tts;

/// <summary>
/// Sintesi vocale italiana reale tramite Windows.Media.SpeechSynthesis.
/// L'audio viene instradato sul dispositivo di uscita selezionato, se presente,
/// altrimenti su quello predefinito di Windows.
/// </summary>
public sealed class ItalianSpeechService : IDisposable
{
    private AudioGraph? _graph;
    private MediaSourceAudioInputNode? _sourceNode;
    private AudioDeviceOutputNode? _outputNode;
    private MediaSource? _mediaSource;
    private DeviceInformation? _outputDevice;
    private bool _disposed;

    /// <summary>Voce italiana selezionata, se presente nel sistema.</summary>
    public string? VoiceName { get; private set; }

    /// <summary>Dispositivo di uscita usato per la lettura vocale.</summary>
    public string? OutputDeviceName => _outputDevice?.Name;

    /// <summary>Imposta il dispositivo di uscita per le prossime frasi.</summary>
    public void SetOutputDevice(DeviceInformation? device)
    {
        Stop();
        _outputDevice = device;
    }

    /// <summary>
    /// Cerca una voce italiana installata in Windows. Restituisce false se non ce n'è alcuna.
    /// </summary>
    public bool Initialize()
    {
        try
        {
            var italianVoices = SpeechSynthesizer.AllVoices
                .Where(v => v.Language.StartsWith("it", StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (italianVoices.Count == 0) return false;
            VoiceName = italianVoices[0].DisplayName;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Legge il testo a voce alta. Una nuova frase interrompe la precedente.
    /// </summary>
    public async Task SpeakAsync(string text, int volume, int rate)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ItalianSpeechService));
        if (string.IsNullOrWhiteSpace(text)) return;
        if (VoiceName is null && !Initialize())
            throw new InvalidOperationException("Nessuna voce italiana installata in Windows.");

        Stop();

        var voice = SpeechSynthesizer.AllVoices
            .FirstOrDefault(v => string.Equals(v.DisplayName, VoiceName, StringComparison.OrdinalIgnoreCase));

        using var synthesizer = new SpeechSynthesizer();
        if (voice is not null) synthesizer.Voice = voice;
        synthesizer.Options.AudioVolume = Math.Clamp(volume / 100.0, 0, 1);
        synthesizer.Options.SpeakingRate = Math.Clamp(rate / 100.0, 0.5, 2.0);

        var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
        var settings = new AudioGraphSettings(AudioRenderCategory.Speech);
        if (_outputDevice is not null) settings.PrimaryRenderDevice = _outputDevice;
        var graphResult = await AudioGraph.CreateAsync(settings);
        if (graphResult.Status != AudioGraphCreationStatus.Success)
            throw new InvalidOperationException($"AudioGraph non disponibile: {graphResult.Status}");

        _graph = graphResult.Graph;

        var outputResult = await _graph.CreateDeviceOutputNodeAsync();
        if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
        {
            var message = $"Nodo audio di uscita non creato: {outputResult.Status}";
            Stop();
            throw new InvalidOperationException(message);
        }
        _outputNode = outputResult.DeviceOutputNode;

        _mediaSource = MediaSource.CreateFromStream(stream, "audio/wav");
        var inputResult = await _graph.CreateMediaSourceAudioInputNodeAsync(_mediaSource);
        if (inputResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
        {
            var message = $"Sorgente audio non creata: {inputResult.Status}";
            Stop();
            throw new InvalidOperationException(message);
        }
        _sourceNode = inputResult.Node;
        _sourceNode.AddOutgoingConnection(_outputNode);

        _graph.Start();
    }

    /// <summary>Interrompe subito la frase in corso.</summary>
    public void Stop()
    {
        _sourceNode?.Dispose();
        _sourceNode = null;

        _outputNode?.Dispose();
        _outputNode = null;

        _mediaSource?.Dispose();
        _mediaSource = null;

        _graph?.Dispose();
        _graph = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
