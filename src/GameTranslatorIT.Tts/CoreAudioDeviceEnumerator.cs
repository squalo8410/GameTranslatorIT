using System.Runtime.InteropServices;

namespace GameTranslatorIT.Tts;

/// <summary>Uscita audio di Windows letta direttamente da Core Audio.</summary>
public sealed record AudioEndpointInfo(string Id, string Name, int State);

/// <summary>
/// Enumera tutti gli endpoint di riproduzione, inclusi quelli disattivati,
/// non collegati o non presenti, che WinRT non restituisce.
/// </summary>
internal sealed class CoreAudioDeviceEnumerator
{
    private const int EDataFlowRender = 0;
    private const int EDataFlowCapture = 1;
    private const int AllStates = 0x0000000F;

    private static readonly Guid ClsMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid PkeyDeviceFriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private static readonly Guid PkeyDeviceInstanceId = new("78C34FC8-104A-4ACA-9EA4-524D52996E57");

    public IReadOnlyList<AudioEndpointInfo> GetRenderEndpoints() => GetEndpoints(EDataFlowRender, "uscita");

    /// <summary>Tutti i microfoni/ingressi audio, inclusi quelli disattivati o non collegati.</summary>
    public IReadOnlyList<AudioEndpointInfo> GetCaptureEndpoints() => GetEndpoints(EDataFlowCapture, "ingresso");

    private IReadOnlyList<AudioEndpointInfo> GetEndpoints(int flow, string label)
    {
        var enumerator = CreateEnumerator();
        IMMDeviceCollection? collection = null;
        var result = new List<AudioEndpointInfo>();
        try
        {
            Check(enumerator.EnumAudioEndpoints(flow, AllStates, out collection),
                $"enumerazione dispositivi di {label}");

            Check(collection.GetCount(out var count), $"conteggio dispositivi di {label}");
            for (var i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    Check(collection.Item(i, out device), $"lettura dispositivo di {label}");
                    Check(device.GetId(out var id), $"lettura ID dispositivo di {label}");
                    Check(device.GetState(out var state), $"lettura stato dispositivo di {label}");
                    result.Add(new AudioEndpointInfo(id, GetFriendlyName(device), state));
                }
                finally
                {
                    if (device is not null) Marshal.ReleaseComObject(device);
                }
            }
            return result;
        }
        finally
        {
            if (collection is not null) Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    public string GetDefaultRenderEndpointId() => GetDefaultEndpointId(EDataFlowRender, "uscita");

    /// <summary>Microfono predefinito di Windows.</summary>
    public string GetDefaultCaptureEndpointId() => GetDefaultEndpointId(EDataFlowCapture, "ingresso");

    private string GetDefaultEndpointId(int flow, string label)
    {
        var enumerator = CreateEnumerator();
        IMMDevice? device = null;
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(flow, 0, out device),
                $"ricerca dispositivo di {label} predefinito");
            Check(device.GetId(out var id), $"lettura ID {label} predefinito");
            return id;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>Legge lo stato attuale di un endpoint (1 = attivo).</summary>
    public int? GetEndpointState(string endpointId)
    {
        var enumerator = CreateEnumerator();
        IMMDevice? device = null;
        try
        {
            if (enumerator.GetDevice(endpointId, out device) < 0) return null;
            return device.GetState(out var state) == 0 ? state : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Prova ad abilitare il nodo dispositivo Windows corrispondente all'endpoint.
    /// Riesce per le uscite disattivate; per quelle scollegate Windows non può creare l'endpoint.
    /// </summary>
    public bool TryEnableEndpoint(string endpointId)
    {
        var enumerator = CreateEnumerator();
        IMMDevice? device = null;
        try
        {
            if (enumerator.GetDevice(endpointId, out device) < 0) return false;

            var instanceId = GetPropertyString(device, PkeyDeviceInstanceId, 256);
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            if (CM_Locate_DevNodeW(out var devNode, instanceId, 0) != 0) return false;

            // 0 = CR_SUCCESS. Se serve un prompt UAC, Windows lo mostra direttamente.
            return CM_Enable_DevNode(ref devNode, 0) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static IMMDeviceEnumerator CreateEnumerator()
        => (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ClsMmDeviceEnumerator, true)!)!;

    private static string GetFriendlyName(IMMDevice device)
        => GetPropertyString(device, PkeyDeviceFriendlyName, 14) ?? "Dispositivo audio";

    private static string? GetPropertyString(IMMDevice device, Guid formatId, uint pid)
    {
        IPropertyStore? store = null;
        try
        {
            Check(device.OpenPropertyStore(0, out store), "apertura proprietà dispositivo");
            var key = new PROPERTYKEY { fmtid = formatId, pid = pid };
            if (store.GetValue(ref key, out var value) != 0) return null;
            try
            {
                return value.vt == 31 && value.pwszVal != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.pwszVal)
                    : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (store is not null) Marshal.ReleaseComObject(store);
        }
    }

    private static void Check(int hresult, string operation)
    {
        if (hresult < 0)
            throw new InvalidOperationException($"{operation} non riuscita (0x{hresult:X8}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pwszVal;
        public long padding1;
        public long padding2;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(
        out uint devNode,
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInstanceId,
        uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Enable_DevNode(ref uint devNode, uint flags);

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr parameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }
}
