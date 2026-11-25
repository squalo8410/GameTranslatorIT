using System.Runtime.InteropServices;

namespace GameTranslatorIT.Core.Models;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public sealed class MonitorInfo
{
    public required int Index { get; init; }
    public required IntPtr Handle { get; init; }
    public required string Name { get; init; }
    public required bool IsPrimary { get; init; }
    public required ScreenRect Bounds { get; init; }
    public required ScreenRect WorkArea { get; init; }

    public override string ToString() =>
        $"{Index}: {Name} {(IsPrimary ? "(principale)" : string.Empty)} {Bounds.Width}x{Bounds.Height}";
}

public sealed class WindowInfo
{
    public required IntPtr Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }
    public required ScreenRect Bounds { get; init; }
    public bool IsVisible { get; init; }

    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? ProcessName : Title;
}

public enum ComponentState
{
    NonInizializzato,
    Pronto,
    Attivo,
    Attesa,
    Avviso,
    Errore,
    Disabilitato
}

public sealed record ComponentStatus(ComponentState State, string Message);
