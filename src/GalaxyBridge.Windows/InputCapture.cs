using System.ComponentModel;
using System.Runtime.InteropServices;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed class InputCapture : IDisposable
{
    private readonly Native.HookProc keyboardCallback, mouseCallback;
    private readonly Native.WinEventProc desktopCallback;
    private nint keyboardHook, mouseHook, desktopHook;
    private readonly KeyboardState keyboard = new();
    private readonly HashSet<byte> physical = [], ignoreUntilReleased = [], pastedKeys = [];
    private readonly System.Windows.Forms.Timer motion = new() { Interval = 16 };
    private IPhoneControl? phone;
    private Form? overlay;
    private Point anchor, returnPoint;
    private nint returnWindow;
    private readonly MouseInputState mouse = new();
    private byte buttons => mouse.Buttons;
    private bool cursorHidden;
    private int returnEpoch, lastDx, lastDy;
    private long enteredAt, motionAt;
    private PhoneSide phoneSide;
    private DesktopBounds laptopBounds;
    private double pendingX, pendingY, sensitivity = 1;
    public bool Active => phone is not null;
    public bool KeyHeld => physical.Count != 0;
    public Func<string?>? PasteText { get; set; }
    public event Action? ToggleRequested;
    public event Action? DesktopChanged;
    public event Action? EdgeReturnRequested;
    public event Action<string>? Error;

    public InputCapture()
    {
        keyboardCallback = KeyboardHook; mouseCallback = MouseHook;
        desktopCallback = (_, _, _, _, _, _, _) => DesktopChanged?.Invoke();
        nint module = Native.GetModuleHandle(null);
        keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, module, 0);
        int keyboardError = keyboardHook == 0 ? Marshal.GetLastWin32Error() : 0;
        mouseHook = Native.SetWindowsHookEx(14, mouseCallback, module, 0);
        int mouseError = mouseHook == 0 ? Marshal.GetLastWin32Error() : 0;
        desktopHook = Native.SetWinEventHook(0x20, 0x20, 0, desktopCallback, 0, 0, 0);
        int desktopError = desktopHook == 0 ? Marshal.GetLastWin32Error() : 0;
        if (keyboardHook == 0 || mouseHook == 0 || desktopHook == 0)
        { Dispose(); throw new Win32Exception(keyboardError != 0 ? keyboardError : mouseError != 0 ? mouseError : desktopError, "Не удалось включить управление и горячую клавишу."); }
        motion.Tick += (_, _) => { FlushMotion(); CheckReturn(); };
    }

    public void Start(IPhoneControl session, double speed, PhoneSide side, bool returnEnabled)
    {
        if (Active) return;
        if (Native.MouseButtonDown) throw new InvalidOperationException("Отпустите кнопки мыши перед переключением.");
        if (physical.Any(key => key != 0x45 && key is not (>= 0xE0 and <= 0xE7)))
            throw new InvalidOperationException("Отпустите остальные клавиши перед переключением.");
        returnPoint = Cursor.Position; returnWindow = Native.GetForegroundWindow();
        Rectangle bounds = Screen.FromPoint(returnPoint).Bounds;
        laptopBounds = new(bounds.X, bounds.Y, bounds.Width, bounds.Height); phoneSide = side;
        enteredAt = Environment.TickCount64; motionAt = 0; lastDx = lastDy = 0;
        anchor = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        sensitivity = Math.Clamp(speed, .25, 4);
        pendingX = pendingY = 0; mouse.Clear(); keyboard.Clear(); pastedKeys.Clear();
        ignoreUntilReleased.Clear(); foreach (byte key in physical) ignoreUntilReleased.Add(key);
        // Keys used to activate capture must not remain pressed in Windows or Android.
        Native.ReleaseHostModifiers();
        overlay = new Form
        {
            Bounds = SystemInformation.VirtualScreen, StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true,
            BackColor = Color.Black, Opacity = .015, Text = "Galaxy Bridge — Ctrl + Alt + F12"
        };
        phone = session;
        try
        {
            overlay.Show();
            _ = Native.SetCursorPos(anchor.X, anchor.Y);
            Cursor.Hide(); cursorHidden = true; motion.Start();
            returnEpoch = returnEnabled ? session.BeginEdgeReturn(side) : 0;
        }
        catch { Stop(); throw; }
    }

    public void Stop()
    {
        if (!Active) return;
        IPhoneControl? previous = phone;
        phone = null; motion.Stop();
        previous?.EndEdgeReturn(); returnEpoch = 0;
        keyboard.Clear(); mouse.Clear(); pendingX = pendingY = 0; pastedKeys.Clear(); ignoreUntilReleased.Clear();
        previous?.ReleaseInputs();
        if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
        overlay?.Close(); overlay?.Dispose(); overlay = null;
        Native.ReleaseHostModifiers();
        _ = Native.SetCursorPos(returnPoint.X, returnPoint.Y);
        if (Native.IsWindow(returnWindow)) _ = Native.SetForegroundWindow(returnWindow);
    }

    public void ResetPhysicalState() => physical.Clear();

    private nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code < 0) return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
        try
        {
            Native.KeyboardData data = Marshal.PtrToStructure<Native.KeyboardData>(lParam);
            if ((data.Flags & 0x10) != 0) return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            bool down = wParam == 0x100 || wParam == 0x104;
            bool up = wParam == 0x101 || wParam == 0x105;
            if (!down && !up) return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            byte usage = ScanCodes.ToUsage(data.Scan, (data.Flags & 1) != 0);
            bool fresh = down && usage != 0 && physical.Add(usage);
            if (up) physical.Remove(usage);
            bool ctrl = physical.Contains(0xE0) || physical.Contains(0xE4);
            bool alt = physical.Contains(0xE2) || physical.Contains(0xE6);
            if (usage == 0x45 && ctrl && alt)
            {
                if (up) ignoreUntilReleased.Remove(usage);
                if (fresh) ToggleRequested?.Invoke();
                return 1;
            }
            if (!Active) return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            if (ignoreUntilReleased.Contains(usage))
            { if (up) ignoreUntilReleased.Remove(usage); return 1; }
            if (pastedKeys.Contains(usage))
            { if (up) pastedKeys.Remove(usage); return 1; }
            // Motion accumulated before a modifier changes must reach Android
            // while that modifier still has its original state (e.g. Shift-drag).
            FlushMotion();
            bool shift = (keyboard.Modifiers & 0x22) != 0;
            bool paste = (usage == 0x19 && (keyboard.Modifiers & 0x11) != 0 && !shift && !alt)
                || (usage == 0x49 && shift && !ctrl && !alt);
            if (fresh && paste && PasteText?.Invoke() is string text)
            {
                // Android KEYCODE_PASTE follows clipboard setting in the same control stream.
                phone?.Send(ControlProtocol.Clipboard(text, paste: true));
                pastedKeys.Add(usage);
                return 1;
            }
            if (usage != 0 && (up || fresh))
            { keyboard.Set(usage, down); phone?.Keyboard(keyboard.Report()); }
            return 1;
        }
        catch (Exception ex)
        {
            Stop(); Error?.Invoke("Управление остановлено: " + ex.GetType().Name);
            return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
        }
    }

    private nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code < 0 || !Active) return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        try
        {
            Native.MouseData data = Marshal.PtrToStructure<Native.MouseData>(lParam);
            if ((data.Flags & 1) != 0) return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
            int message = (int)wParam;
            if (message == 0x200)
            {
                int dx = data.Point.X - anchor.X, dy = data.Point.Y - anchor.Y;
                if (dx != 0 || dy != 0)
                { pendingX += dx * sensitivity; pendingY += dy * sensitivity; _ = Native.SetCursorPos(anchor.X, anchor.Y); }
            }
            else if (MouseInputState.Handles(message))
            {
                // Pending motion belongs to the old button state, before DOWN/UP.
                FlushMotion();
                if (mouse.Update(message, data.MouseDataValue, out MouseUpdate update))
                    phone?.Mouse(update.Buttons, 0, 0, update.Wheel, update.HorizontalWheel);
            }
            return 1;
        }
        catch (Exception ex)
        { Stop(); Error?.Invoke("Управление остановлено: " + ex.GetType().Name); return Native.CallNextHookEx(mouseHook, code, wParam, lParam); }
    }

    private void FlushMotion()
    {
        int dx = (int)Math.Truncate(pendingX), dy = (int)Math.Truncate(pendingY);
        pendingX -= dx; pendingY -= dy;
        if (dx != 0 || dy != 0)
        { lastDx = dx; lastDy = dy; motionAt = Environment.TickCount64; phone?.Mouse(buttons, dx, dy); }
    }
    private void CheckReturn()
    {
        PhoneEdgeSample? sample = phone?.LatestEdge;
        if (sample is null || returnEpoch < 1 || !EdgeReturnPolicy.CanReturn(sample, returnEpoch, phoneSide,
            lastDx, lastDy, buttons, KeyHeld, enteredAt, motionAt, Environment.TickCount64)) return;
        (int x, int y) = EdgeReturnPolicy.LaptopPosition(laptopBounds, phoneSide, sample);
        returnPoint = new Point(x, y); returnEpoch = 0;
        if (EdgeReturnRequested is not null) EdgeReturnRequested.Invoke(); else Stop();
    }
    public void Dispose()
    {
        Stop(); motion.Dispose();
        if (keyboardHook != 0) Native.UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
        if (desktopHook != 0) Native.UnhookWinEvent(desktopHook);
        keyboardHook = mouseHook = desktopHook = 0;
    }
}
