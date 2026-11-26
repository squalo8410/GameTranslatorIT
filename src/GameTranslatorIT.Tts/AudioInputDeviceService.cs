using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;

namespace GameTranslatorIT.Tts;

/// <summary>Microfono/ingresso audio selezionabile nell'interfaccia.</summary>
public sealed record AudioInputDevice(
    string Id,
    string Name,
    string Status,
    bool IsDefault,
    bool IsActive)
{
    public string DisplayName => IsDefault ? $"{Name} — predefinito ({Status})" : $"{Name} ({Status})";
    public override string ToString() => DisplayName;
}

/// <summary>
/// Elenco reale degli ingressi audio di Windows (microfoni, cuffie con microfono,
/// dispositivi virtuali), attivi e non, letto da Core Audio e WinRT.
/// </summary>
public static partial class AudioInputDeviceService
{
    public static async Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync()
    {
        var enumerator = new CoreAudioDeviceEnumerator();
        var endpoints = enumerator.GetCaptureEndpoints();
        string defaultId;
        try { defaultId = enumerator.GetDefaultCaptureEndpointId(); }
        catch { defaultId = string.Empty; }

        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var winRtDevices = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
            foreach (var device in winRtDevices) activeIds.Add(device.Id);
        }
        catch
        {
            // WinRT non disponibile: Core Audio resta la fonte degli endpoint.
        }

        var defaultKey = ExtractKey(defaultId);
        var result = endpoints.Select(endpoint =>
        {
            var key = ExtractKey(endpoint.Id);
            var active = endpoint.State == 1 || activeIds.Contains(endpoint.Id) ||
                         (key is not null && activeIds.Any(id => string.Equals(ExtractKey(id), key, StringComparison.OrdinalIgnoreCase)));
            return new AudioInputDevice(
                endpoint.Id,
                endpoint.Name,
                StatusOf(endpoint.State, active),
                key is not null && string.Equals(key, defaultKey, StringComparison.OrdinalIgnoreCase),
                active);
        });

        return result
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.IsActive ? 0 : 1)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string StatusOf(int state, bool active) => active && state == 1
        ? "attivo"
        : state switch
        {
            2 => "disattivato",
            4 => "non presente",
            8 => "non collegato",
            _ => active ? "attivo" : $"stato {state}"
        };

    [GeneratedRegex(@"\{([0-9a-fA-F-]{36})\}")]
    private static partial Regex GuidPattern();

    private static string? ExtractKey(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var match = GuidPattern().Match(id);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : id.ToLowerInvariant();
    }
}
