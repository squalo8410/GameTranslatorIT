using System.Runtime.InteropServices;
using System.Text;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Capture;

/// <summary>
/// Enumerazione reale di monitor e finestre tramite Win32. Nessun dato simulato.
/// </summary>
public static class Win32DisplayEnumerator
{
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        var callback = new MonitorEnumProc((IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                result.Add(new MonitorInfo
                {
                    Index = result.Count,
                    Handle = hMonitor,
                    Name = $"Monitor {result.Count + 1}",
                    IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    Bounds = ToRect(info.rcMonitor),
                    WorkArea = ToRect(info.rcWork)
                });
            }
            return true;
        });

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return result;
    }

    public static IReadOnlyList<WindowInfo> GetWindows()
    {
        var result = new List<WindowInfo>();
        var callback = new EnumWindowsProc((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var length = GetWindowTextLengthW(hWnd);
            if (length <= 0)
                return true;

            var builder = new StringBuilder(length + 1);
            _ = GetWindowTextW(hWnd, builder, builder.Capacity);
            var title = builder.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            string processName;
            try
            {
                processName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                processName = "sconosciuto";
            }

            if (GetWindowRect(hWnd, out var rect))
            {
                result.Add(new WindowInfo
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessName = processName,
                    Bounds = ToRect(rect),
                    IsVisible = true
                });
            }
            return true;
        });

        EnumWindows(callback, IntPtr.Zero);
        return result.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
