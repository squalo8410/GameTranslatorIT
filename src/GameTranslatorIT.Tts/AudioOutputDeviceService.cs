using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;

namespace GameTranslatorIT.Tts;

/// <summary>Dispositivo di uscita audio selezionabile nell'interfaccia.</summary>
public sealed record AudioOutputDevice(
    string Id,
    string Name,
    string Status,
    bool IsDefault,
    bool IsActive,
    bool VolumeAvailable,
    DeviceInformation? Device)
{
    /// <summary>Testo mostrato nel menu a tendina. Tutte le uscite restano scegliibili.</summary>
    public string DisplayName
    {
        get
        {
            var detail = IsActive
                ? Status
                : Status switch
                {
                    "disattivato" => "da attivare",
                    "non collegato" => "da collegare",
                    "non presente" => "non presente",
                    _ => $"{Status}, da verificare"
                };
            return IsDefault ? $"{Name} — predefinito ({detail})" : $"{Name} ({detail})";
        }
    }

    /// <summary>Spiegazione mostrata quando il dispositivo non è ancora attivo.</summary>
    public string AvailabilityHint => IsActive
        ? "Uscita attiva: può essere usata per voce e volume."
        : "Selezionandolo l'app proverà ad attivarlo; se il dispositivo fisico non è collegato, apre le impostazioni audio di Windows e lo usa appena diventa attivo.";

    public override string ToString() => DisplayName;
}

/// <summary>
/// Elenco completo delle uscite audio: attive, disattivate, non collegate e non presenti.
/// Core Audio fornisce tutti gli endpoint; WinRT fornisce l'oggetto necessario
/// per instradare la sintesi vocale su quelli attivi.
/// </summary>
public static partial class AudioOutputDeviceService
{
    /// <summary>Restituisce tutte le uscite audio di Windows, ordinata per disponibilità.</summary>
    public static async Task<IReadOnlyList<AudioOutputDevice>> GetDevicesAsync()
    {
        var coreAudio = new CoreAudioDeviceEnumerator();
        var endpoints = coreAudio.GetRenderEndpoints();
        var defaultEndpointId = coreAudio.GetDefaultRenderEndpointId();
        var defaultKey = ExtractKey(defaultEndpointId);
        var volumeController = new SystemVolumeController();

        var activeDevices = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender);
        var activeByKey = new Dictionary<string, DeviceInformation>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in activeDevices)
        {
            var key = ExtractKey(device.Id);
            if (key is not null) activeByKey[key] = device;
        }

        var result = new List<AudioOutputDevice>(endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            var key = ExtractKey(endpoint.Id);
            activeByKey.TryGetValue(key ?? string.Empty, out var winRtDevice);

            var isActive = endpoint.State == 1;
            var volumeAvailable = true;
            try
            {
                volumeController.GetVolume(endpoint.Id);
            }
            catch
            {
                volumeAvailable = false;
            }

            result.Add(new AudioOutputDevice(
                endpoint.Id,
                endpoint.Name,
                StatusOf(endpoint.State),
                string.Equals(key, defaultKey, StringComparison.OrdinalIgnoreCase),
                isActive,
                volumeAvailable,
                isActive ? winRtDevice : null));
        }

        return result
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.IsActive ? 0 : 1)
            .ThenBy(device => StatusOrder(device.Status))
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string StatusOf(int state) => state switch
    {
        1 => "attivo",
        2 => "disattivato",
        4 => "non presente",
        8 => "non collegato",
        _ => $"stato {state}"
    };

    private static int StatusOrder(string status) => status switch
    {
        "attivo" => 0,
        "disattivato" => 1,
        "non collegato" => 2,
        "non presente" => 3,
        _ => 4
    };

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    private static string? ExtractKey(string id) => GuidPattern().Match(id).Value is { Length: > 0 } key ? key : null;
}
