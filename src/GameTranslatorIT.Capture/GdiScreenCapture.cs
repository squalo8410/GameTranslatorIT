using System.Runtime.InteropServices;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.Capture;

/// <summary>
/// Cattura GDI reale di una regione dello schermo in formato BGRA8.
/// Funziona con finestro/borderless perché legge il frame già composto sul desktop.
/// In schermo intero esclusivo il contenuto può non essere disponibile: l'app lo rileva e avvisa.
/// </summary>
public sealed class GdiScreenCapture : IDisposable
{
    private const int SrcCopy = 0x00CC0020;
    private const int DibRgbColors = 0;
    private bool _disposed;

    /// <summary>
    /// Cattura l'intera regione richiesta e ne restituisce pixel + hash di cambiamento.
    /// </summary>
    public CapturedFrame CaptureRegion(ScreenRect area)
    {
        ThrowIfDisposed();

        var width = Math.Max(1, area.Width);
        var height = Math.Max(1, area.Height);
        var stride = width * 4;
        var imageBytes = stride * height;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new InvalidOperationException("Impossibile ottenere il DC dello schermo.");

        var memDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        var oldBitmap = IntPtr.Zero;
        var buffer = IntPtr.Zero;

        try
        {
            if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException("Impossibile creare il buffer GDI compatibile.");

            oldBitmap = SelectObject(memDc, bitmap);
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, area.Left, area.Top, SrcCopy))
                throw new InvalidOperationException("BitBlt non riuscita.");

            var bitmapInfo = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = width,
                    Height = -height, // top-down
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = imageBytes
                }
            };

            buffer = Marshal.AllocHGlobal(imageBytes);
            var copied = GetDIBits(memDc, bitmap, 0, (uint)height, buffer, ref bitmapInfo, DibRgbColors);
            if (copied == 0) throw new InvalidOperationException("GetDIBits non riuscita.");

            var pixels = new byte[imageBytes];
            Marshal.Copy(buffer, pixels, 0, imageBytes);

            return new CapturedFrame(pixels, width, height, area, ComputeFnv1A(pixels, stride), DateTimeOffset.UtcNow);
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Calcola un hash rapido campionando i pixel (1 ogni 4 righe e 1 ogni 4 colonne).
    /// </summary>
    private static ulong ComputeFnv1A(byte[] data, int stride)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;

        for (var y = 0; y < data.Length / stride; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < stride; x += 16)
            {
                hash ^= data[row + x];
                hash *= prime;
                hash ^= data[row + x + 1];
                hash *= prime;
                hash ^= data[row + x + 2];
                hash *= prime;
            }
        }
        return hash;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GdiScreenCapture));
    }

    public void Dispose() => _disposed = true;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER Header;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hDc, int x, int y, int width, int height,
        IntPtr hSrcDc, int xSrc, int ySrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hDc, IntPtr hBitmap, uint start, uint count,
        IntPtr lpBits, ref BITMAPINFO lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hDc);
}
