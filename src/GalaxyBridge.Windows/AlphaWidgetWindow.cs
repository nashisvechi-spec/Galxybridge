using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GalaxyBridge.Windows;

// Painting must not resize/move a Form while WinForms is processing its bounds.
internal static class AlphaWidgetWindow
{
    public static bool Present(IntPtr window, Bitmap bitmap, byte opacity)
    {
        if (!GetWindowRect(window, out WindowRect bounds)) throw new Win32Exception(Marshal.GetLastWin32Error());
        // A resize/DPI callback can still have the previous client dimensions.
        // Discard that frame instead of feeding its size back into the HWND.
        if (bitmap.Width != bounds.Right - bounds.Left || bitmap.Height != bounds.Bottom - bounds.Top) return false;
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, image = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (bitmap.PixelFormat != PixelFormat.Format32bppPArgb) throw new ArgumentException("Expected premultiplied bitmap.", nameof(bitmap));
            image = bitmap.GetHbitmap(Color.FromArgb(0));previous = SelectObject(memory, image);
            Point source = Point.Empty;
            Blend blend = new() { SourceConstantAlpha = opacity, AlphaFormat = 1 };
            // NULL position/size keep the geometry owned by WinForms unchanged.
            if (!UpdateLayeredWindow(window, screen, IntPtr.Zero, IntPtr.Zero, memory, ref source, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return true;
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
            if (image != IntPtr.Zero) DeleteObject(image);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }
    public static void Move(IntPtr window)
    { ReleaseCapture();SendMessage(window, 0x00A1, new IntPtr(2), IntPtr.Zero); } // WM_NCLBUTTONDOWN / HTCAPTION
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Blend { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, IntPtr position, IntPtr size,
        IntPtr sourceDc, ref Point source, uint colorKey, ref Blend blend, uint flags);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
