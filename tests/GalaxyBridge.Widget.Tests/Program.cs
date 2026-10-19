using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GalaxyBridge.Windows;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
    }
    private static void Pump()
    {
        for (int i = 0; i < 5; i++) Application.DoEvents();
    }
    private static void CheckGeometry(FileTransferWidget window)
    {
        Size expected = FileWidgetLayout.SizeForDpi(window.DeviceDpi, Screen.FromControl(window).WorkingArea.Size);
        Check(window.Size == expected, $"Widget at {window.DeviceDpi} DPI: {window.Size}, expected {expected}");
        Check(window.ClientSize == window.Size, "Widget must have no nonclient frame");
        Check(GetWindowRect(window.Handle, out NativeRect native), "Cannot read actual HWND bounds");
        Check(new Size(native.Right - native.Left, native.Bottom - native.Top) == expected, "Native bounds diverged from managed size");
    }
    private static void CheckVisibleFrame(FileTransferWidget window)
    {
        Pump();Check(window.Visible && IsWindowVisible(window.Handle), "Widget HWND is hidden");
        Check(window.PresentedFrames > 0, "Widget has no accepted first frame");
        float scale = FileWidgetLayout.Viewport(window.ClientSize).Width / FileWidgetLayout.ArtWidth;
        Point body = window.PointToScreen(new Point((int)(24 * scale), (int)(80 * scale)));
        Check(WindowFromPoint(body) == window.Handle, "Opaque widget body is not present/hittable on the desktop");
        Point corner = window.PointToScreen(Point.Empty);
        Check(WindowFromPoint(corner) != window.Handle, "Transparent corner intercepts the desktop");
    }
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        int sendCalls = 0, cancelCalls = 0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using FileTransferWidget window = new(_ => { sendCalls++;return Task.CompletedTask; }, () => cancelCalls++);
            window.Location = Screen.PrimaryScreen!.WorkingArea.Location;
            // Match production: status/tooltips and transparency are set before Show.
            window.UpdateTransfer(true, false, "Телефон: S25", "", null);window.SetTransparency(attempt == 1 ? 40 : 0);
            window.ShowWidget();Pump();CheckGeometry(window);CheckVisibleFrame(window);
            window.Hide();window.ShowWidget();Pump();CheckGeometry(window);CheckVisibleFrame(window);
            for (int frame = 0; frame < 24; frame++)
            {
                window.UpdateTransfer(true, frame % 2 == 0, "Телефон: S25", "photo.jpg", frame % 3 == 0 ? null : frame * 4);
                window.SetTransparency(frame % 2 == 0 ? 80 : 0);Pump();CheckGeometry(window);
            }
            CheckVisibleFrame(window);
            // Simulate stale bounds from creation/resize before the next layout turn.
            window.ClientSize = new Size(window.Width, 56);Pump();CheckGeometry(window);
            Rectangle before = window.Bounds;
            using (Bitmap stale = new(window.Width, window.Height - 32, PixelFormat.Format32bppPArgb))
                Check(!AlphaWidgetWindow.Present(window.Handle, stale, 255), "Stale client frame must be discarded");
            Check(window.Bounds == before, "Present resized/moved the window from a stale client frame");
            CheckGeometry(window);
            foreach (int dpi in new[] { 96, 120, 144, 192, 144, 96 })
            {
                IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
                try
                {
                    NativeRect suggested = new() { Left = window.Left, Top = window.Top, Right = window.Left + 320, Bottom = window.Top + 56 };
                    Marshal.StructureToPtr(suggested, pointer, false);
                    SendMessage(window.Handle, 0x02E0, new IntPtr(dpi | dpi << 16), pointer); // WM_DPICHANGED
                }
                finally { Marshal.FreeHGlobal(pointer); }
                Pump();CheckGeometry(window);
            }
            CheckVisibleFrame(window);
            window.Close();Check(window.IsDisposed, "Widget did not close");
        }
        Check(sendCalls == 0 && cancelCalls == 0, "Layout/progress/close must not send or cancel files");
        Console.WriteLine($"PASS: {checks} native Windows widget assertions");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
}
