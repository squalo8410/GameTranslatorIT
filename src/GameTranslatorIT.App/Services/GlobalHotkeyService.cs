using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace GameTranslatorIT.App.Services;

/// <summary>
/// Tasti rapidi globali per i riquadri:
/// F1–F8 muto, Ctrl+F1–F8 selezione, Ctrl+Shift+F1–F8 cambio modalità,
/// Ctrl+Alt+F1–F8 rimozione selezione, Ctrl+Shift+P pannello rapido,
/// Ctrl+Shift+D pannello di controllo completo.
/// Usa una finestra nascosta dedicata: riceve WM_HOTKEY anche quando la finestra
/// principale non è attiva e non dipende dall'hook della finestra WPF.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const int VkF1 = 0x70;
    private const int VkP = 0x50;
    private const int VkD = 0x44;
    private const int MaxRegions = 8;
    private const int QuickControlId = 500;
    private const int ControlPanelId = 501;
    private static readonly IntPtr HwndMessage = new(-3);

    private HwndSource? _source;
    private readonly List<int> _registered = new();
    private Action<int>? _toggleMute;
    private Action<int>? _select;
    private Action<int>? _cycleMode;
    private Action<int>? _remove;
    private Action? _toggleQuickControl;
    private Action? _toggleControlPanel;
    private bool _disposed;

    public int RegisteredCount => _registered.Count;

    public bool Attach(
        IntPtr unusedMainWindowHandle,
        Action<int> toggleMute,
        Action<int> select,
        Action<int> cycleMode,
        Action<int> remove,
        Action? toggleQuickControl = null,
        Action? toggleControlPanel = null)
    {
        Detach();

        _toggleMute = toggleMute;
        _select = select;
        _cycleMode = cycleMode;
        _remove = remove;
        _toggleQuickControl = toggleQuickControl;
        _toggleControlPanel = toggleControlPanel;

        _source = new HwndSource(0, 0, 0, 0, 0, "GameTranslatorIT.Hotkeys", HwndMessage);
        _source.AddHook(WndProc);

        for (var i = 0; i < MaxRegions; i++)
        {
            var key = VkF1 + i;
            Add(100 + i, 0, key);
            Add(200 + i, ModControl, key);
            Add(300 + i, ModControl | ModShift, key);
            Add(400 + i, ModControl | ModAlt, key);
        }

        if (_toggleQuickControl is not null)
            Add(QuickControlId, ModControl | ModShift, VkP);
        if (_toggleControlPanel is not null)
            Add(ControlPanelId, ModControl | ModShift, VkD);

        return _registered.Count > 0;
    }

    private void Add(int id, uint modifiers, int key)
    {
        if (RegisterHotKey(_source!.Handle, id, modifiers, key))
            _registered.Add(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotKey) return IntPtr.Zero;

        var id = wParam.ToInt32();
        if (id == QuickControlId)
        {
            handled = true;
            _toggleQuickControl?.Invoke();
            return IntPtr.Zero;
        }
        if (id == ControlPanelId)
        {
            handled = true;
            _toggleControlPanel?.Invoke();
            return IntPtr.Zero;
        }

        var index = id % 100;
        if (index < 0 || index >= MaxRegions) return IntPtr.Zero;

        handled = true;
        switch (id / 100)
        {
            case 1: _toggleMute?.Invoke(index); break;
            case 2: _select?.Invoke(index); break;
            case 3: _cycleMode?.Invoke(index); break;
            case 4: _remove?.Invoke(index); break;
        }
        return IntPtr.Zero;
    }

    public void Detach()
    {
        if (_source is not null)
        {
            foreach (var id in _registered)
                UnregisterHotKey(_source.Handle, id);
            _registered.Clear();
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        _toggleMute = null;
        _select = null;
        _cycleMode = null;
        _remove = null;
        _toggleQuickControl = null;
        _toggleControlPanel = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Detach();
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
