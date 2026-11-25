using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Esclude le finestre dell'app dalla cattura GDI del desktop, documentata da Windows
/// come WDA_EXCLUDEFROMCAPTURE. La finestra resta visibile all'utente, ma non compare
/// nei frame catturati e quindi non può essere rilevata come sottotitolo.
/// </summary>
internal static class WindowCaptureExclusion
{
    private const uint WdaNone = 0x00000000;
    private const uint WdaExcludeFromCapture = 0x00000011;

    public static bool Exclude(Window? window) => Apply(window, WdaExcludeFromCapture);

    public static bool Restore(Window? window) => Apply(window, WdaNone);

    private static bool Apply(Window? window, uint affinity)
    {
        if (window is null) return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        return SetWindowDisplayAffinity(hwnd, affinity);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
