using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GameTranslatorIT.Tts;

/// <summary>
/// Controllo reale del volume principale di Windows tramite l'endpoint audio di riproduzione predefinito.
/// Legge e scrive IAudioEndpointVolume: nessun comando simulato.
/// </summary>
public sealed class SystemVolumeController
{
    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 0;
    private const int CLSCTX_ALL = 23;

    private static readonly Guid ClsMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    /// <summary>Legge il volume dell'uscita scelta, o di quella predefinita (0-100).</summary>
    public int GetVolume(string? deviceId = null)
    {
        var endpoint = GetEndpoint(deviceId);
        try
        {
            Check(endpoint.GetMasterVolumeLevelScalar(out var level), "lettura volume Windows");
            return (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100);
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    /// <summary>Imposta il volume dell'uscita scelta, o di quella predefinita (0-100).</summary>
    public void SetVolume(int value, string? deviceId = null)
    {
        var level = Math.Clamp(value, 0, 100) / 100f;
        var endpoint = GetEndpoint(deviceId);
        try
        {
            Check(endpoint.SetMasterVolumeLevelScalar(level, IntPtr.Zero), "impostazione volume Windows");

            if (level > 0)
            {
                Check(endpoint.GetMute(out var muted), "lettura stato muto Windows");
                if (muted)
                    Check(endpoint.SetMute(false, IntPtr.Zero), "rimozione muto Windows");
            }
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    /// <summary>Legge lo stato muto dell'uscita scelta.</summary>
    public bool GetMute(string? deviceId = null)
    {
        var endpoint = GetEndpoint(deviceId);
        try
        {
            Check(endpoint.GetMute(out var muted), "lettura stato muto Windows");
            return muted;
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    /// <summary>Imposta lo stato muto dell'uscita scelta.</summary>
    public void SetMute(bool muted, string? deviceId = null)
    {
        var endpoint = GetEndpoint(deviceId);
        try
        {
            Check(endpoint.SetMute(muted, IntPtr.Zero), "impostazione stato muto Windows");
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    private static IAudioEndpointVolume GetEndpoint(string? deviceId)
    {
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ClsMmDeviceEnumerator, true)!)!;
        IMMDevice? device = null;
        try
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                Check(enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out device),
                    "ricerca dispositivo di uscita predefinito");
            }
            else
            {
                var endpointId = ToEndpointId(deviceId);
                Check(enumerator.GetDevice(endpointId, out device),
                    $"ricerca dispositivo di uscita {endpointId}");
            }

            Check(device.Activate(IidAudioEndpointVolume, CLSCTX_ALL, IntPtr.Zero, out var comObject),
                "attivazione controllo volume");

            if (comObject is not IAudioEndpointVolume endpoint)
                throw new InvalidOperationException("Controllo volume Windows non disponibile.");
            return endpoint;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static void Check(int hresult, string operation)
    {
        if (hresult < 0)
            throw new InvalidOperationException($"{operation} non riuscita (0x{hresult:X8}).");
    }

    /// <summary>
    /// Gli ID restituiti da DeviceInformation includono un prefisso WinRT;
    /// Core Audio si aspetta solo l'endpoint MMDevice.
    /// </summary>
    private static string ToEndpointId(string deviceId)
    {
        var match = Regex.Match(deviceId, @"\{[0-9.]+\}\.\{[0-9a-fA-F-]+\}");
        return match.Success ? match.Value : deviceId;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int channel, float levelDb, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(int channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out int step, out int stepCount);
        [PreserveSig] int VolumeStepUp(IntPtr context);
        [PreserveSig] int VolumeStepDown(IntPtr context);
        [PreserveSig] int QueryHardwareSupport(out int mask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }
}
