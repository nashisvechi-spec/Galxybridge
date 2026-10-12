using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GalaxyBridge.Windows;

// Per-pixel alpha preserves the rounded silhouette and glow, without a color key.
internal static class AlphaWidgetWindow
{
    public static void Present(IntPtr window, Bitmap bitmap, Point location)
    {
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, image = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (bitmap.PixelFormat != PixelFormat.Format32bppPArgb) throw new ArgumentException("Expected premultiplied bitmap.", nameof(bitmap));
            image = bitmap.GetHbitmap(Color.FromArgb(0));previous = SelectObject(memory, image);
            Point source = Point.Empty;Size size = bitmap.Size;
            Blend blend = new() { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(window, screen, ref location, ref size, memory, ref source, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
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
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref Point position, ref Size size,
        IntPtr sourceDc, ref Point source, uint colorKey, ref Blend blend, uint flags);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
