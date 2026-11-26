using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Capture;

/// <summary>
/// Risolve l'area di cattura in modo stabile: handle e PID prima del titolo,
/// così due finestre con lo stesso nome o una finestra rinominata non portano
/// a leggere la schermata sbagliata.
/// </summary>
public static class GameWindowResolver
{
    public static ScreenRect ResolveArea(
        CaptureMode mode,
        int monitorIndex,
        long windowHandle,
        int windowPid,
        string? windowTitle,
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<WindowInfo> windows)
    {
        if (mode == CaptureMode.FullScreen && monitors.Count > 0)
            return monitors[Math.Clamp(monitorIndex, 0, monitors.Count - 1)].Bounds;

        WindowInfo? window = null;
        if (windowHandle != 0)
        {
            var handle = new IntPtr(windowHandle);
            window = windows.FirstOrDefault(w => w.Handle == handle)
                     ?? Win32DisplayEnumerator.GetWindowByHandle(handle);
        }

        window ??= windowPid > 0
            ? windows.FirstOrDefault(w => w.Pid == windowPid)
            : null;

        window ??= !string.IsNullOrWhiteSpace(windowTitle)
            ? windows.FirstOrDefault(w => string.Equals(w.Title, windowTitle, StringComparison.OrdinalIgnoreCase))
            : null;

        if (window is not null && window.Bounds.Width > 0 && window.Bounds.Height > 0)
            return window.Bounds;

        return monitors.Count > 0 ? monitors[0].Bounds : default;
    }
}
