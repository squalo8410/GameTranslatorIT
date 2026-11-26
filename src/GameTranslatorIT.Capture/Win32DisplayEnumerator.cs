using System.Runtime.InteropServices;
using System.Text;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Capture;

/// <summary>
/// Enumerazione reale di monitor e finestre tramite Win32. Nessun dato simulato.
/// Include finestre senza titolo, esclude quelle chiuse/minimizzate/cloaked e
/// riconosce la finestra attiva per il rilevamento automatico del gioco.
/// </summary>
public static class Win32DisplayEnumerator
{
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int DwmwaCloaked = 14;
    private const int DwmwaExtendedFrameBounds = 9;
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow"
    };

    static Win32DisplayEnumerator()
    {
        // Il per-monitor DPI awareness evita rettangoli sbagliati con monitor misti.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch { /* Windows precedente o già impostato: si usa GetWindowRect come fallback. */ }
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var found = new List<(IntPtr Handle, string Name, bool IsPrimary, ScreenRect Bounds, ScreenRect WorkArea)>();
        var callback = new MonitorEnumProc((IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                var display = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                var name = $"Monitor {found.Count + 1}";
                if (EnumDisplayDevicesW(null, (uint)found.Count, ref display, 0) &&
                    !string.IsNullOrWhiteSpace(display.DeviceName))
                {
                    var friendly = display.DeviceString?.Trim();
                    name = string.IsNullOrWhiteSpace(friendly) || friendly.Equals("Monitor", StringComparison.OrdinalIgnoreCase)
                        ? display.DeviceName.Trim()
                        : $"{display.DeviceName.Trim()} — {friendly}";
                }

                found.Add((hMonitor, name, (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    ToRect(info.rcMonitor), ToRect(info.rcWork)));
            }
            return true;
        });

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        // Principale prima, poi per posizione: l'indice salvato resta stabile.
        var ordered = found
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.Top)
            .ThenBy(m => m.Bounds.Left)
            .ToList();

        var result = new List<MonitorInfo>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var monitor = ordered[index];
            result.Add(new MonitorInfo
            {
                Index = index,
                Handle = monitor.Handle,
                Name = monitor.Name,
                IsPrimary = monitor.IsPrimary,
                Bounds = monitor.Bounds,
                WorkArea = monitor.WorkArea
            });
        }
        return result;
    }

    /// <summary>Tutte le finestre reali utilizzabili per la cattura del gioco.</summary>
    public static IReadOnlyList<WindowInfo> GetWindows(int? excludePid = null)
    {
        var result = new List<WindowInfo>();
        var seen = new HashSet<IntPtr>();
        var processNames = new Dictionary<uint, string>();

        var callback = new EnumWindowsProc((hWnd, _) =>
        {
            if (TryCreateWindowInfo(hWnd, excludePid, processNames, out var window) && seen.Add(hWnd))
                result.Add(window!);
            return true;
        });

        EnumWindows(callback, IntPtr.Zero);
        return result
            .OrderByDescending(w => string.Equals(w.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Finestra attiva in primo piano: usata per rilevare il gioco automaticamente.</summary>
    public static WindowInfo? GetForegroundWindowInfo(int? excludePid = null)
        => TryCreateWindowInfo(GetForegroundWindow(), excludePid, new Dictionary<uint, string>(), out var window)
            ? window
            : null;

    /// <summary>Risolve di nuovo un handle salvato, per verificare che la finestra esista ancora.</summary>
    public static WindowInfo? GetWindowByHandle(IntPtr handle, int? excludePid = null)
        => handle != IntPtr.Zero && TryCreateWindowInfo(handle, excludePid, new Dictionary<uint, string>(), out var window)
            ? window
            : null;

    private static bool TryCreateWindowInfo(
        IntPtr hWnd,
        int? excludePid,
        Dictionary<uint, string> processNames,
        out WindowInfo? window)
    {
        window = null;
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsIconic(hWnd)) return false;

        var className = ReadClassName(hWnd);
        if (ShellClasses.Contains(className)) return false;

        var cloaked = 0;
        if (DwmGetWindowAttribute(hWnd, DwmwaCloaked, out cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        GetWindowThreadProcessId(hWnd, out var pid);
        if (excludePid is { } skipped && pid == (uint)skipped) return false;

        if (!processNames.TryGetValue(pid, out var processName))
        {
            processName = ResolveProcessName(pid);
            processNames[pid] = processName;
        }

        var builder = new StringBuilder(GetWindowTextLengthW(hWnd) + 1);
        _ = GetWindowTextW(hWnd, builder, builder.Capacity);
        var title = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(title)) title = $"{processName} (senza titolo)";
        if (title.StartsWith("MainWindow", StringComparison.OrdinalIgnoreCase) &&
            processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
            title = $"{processName} — {title}";

        if (!TryGetBounds(hWnd, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0) return false;

        window = new WindowInfo
        {
            Handle = hWnd,
            Title = title,
            ProcessName = processName,
            Pid = (int)pid,
            Bounds = bounds,
            IsVisible = true
        };
        return true;
    }

    private static string ResolveProcessName(uint pid)
    {
        if (pid == 0) return "sconosciuto";
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return string.IsNullOrWhiteSpace(process.ProcessName) ? $"processo {pid}" : process.ProcessName;
        }
        catch
        {
            return $"processo {pid}";
        }
    }

    private static string ReadClassName(IntPtr hWnd)
    {
        var builder = new StringBuilder(128);
        _ = GetClassNameW(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool TryGetBounds(IntPtr hWnd, out ScreenRect bounds)
    {
        if (DwmGetWindowAttribute(hWnd, DwmwaExtendedFrameBounds, out RECT extended, Marshal.SizeOf<RECT>()) == 0 &&
            extended.Right > extended.Left && extended.Bottom > extended.Top)
        {
            bounds = ToRect(extended);
            return true;
        }

        if (GetWindowRect(hWnd, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
        {
            bounds = ToRect(rect);
            return true;
        }

        bounds = default;
        return false;
    }

    private static ScreenRect ToRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private const int MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string? DeviceKey;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
