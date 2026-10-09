using System.Runtime.InteropServices;
using System.Text;
using GalaxyBridge.Core;
namespace GalaxyBridge.Windows;
internal static class LanTextInput
{
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint code, uint type, nint layout);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint vk, uint scan, byte[] keys, StringBuilder text, int count, uint flags, nint layout);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    public static string? Translate(byte usage, byte modifiers)
    {
        for (uint scan = 1; scan <= 0x58; scan++)
        {
            if (ScanCodes.ToUsage(scan, false) != usage) continue;
            nint layout = GetKeyboardLayout(0); uint vk = MapVirtualKeyEx(scan, 3, layout);
            byte[] keys = new byte[256];
            if ((modifiers & 0x22) != 0) keys[0x10] = 0x80;
            keys[0x14] = (byte)(GetKeyState(0x14) & 1); keys[vk & 255] |= 0x80;
            StringBuilder text = new(16);
            int n = ToUnicodeEx(vk, scan, keys, text, 16, 4, layout); // Win10: do not modify dead-key state.
            return n > 0 ? text.ToString(0, Math.Min(n, text.Length)) : null;
        }
        return null;
    }
}
