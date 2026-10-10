namespace GalaxyBridge.Core;

// Translate low-level Win32 hook messages without substituting Android actions.
public sealed class MouseInputState
{
    public byte Buttons { get; private set; }
    private int wheelRemainder, horizontalRemainder;
    public static bool Handles(int message) => message is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20B or 0x20C or 0x20A or 0x20E;
    public bool Update(int message, uint data, out MouseUpdate update)
    {
        update = default;
        if (!Handles(message)) return false;
        if (message is 0x20A or 0x20E)
        {
            int delta = (short)(data >> 16);
            ref int remainder = ref (message == 0x20A ? ref wheelRemainder : ref horizontalRemainder);
            remainder += delta;
            int steps = remainder / 120; remainder %= 120;
            if (steps == 0) return false;
            update = new(Buttons, message == 0x20A ? steps : 0, message == 0x20E ? steps : 0);
            return true;
        }
        byte mask;
        if (message is 0x20B or 0x20C)
        {
            mask = (data >> 16) switch { 1 => (byte)8, 2 => (byte)16, _ => (byte)0 };
            if (mask == 0) return false;
        }
        else mask = message is 0x201 or 0x202 ? (byte)1 : message is 0x204 or 0x205 ? (byte)2 : (byte)4;
        bool down = message is 0x201 or 0x204 or 0x207 or 0x20B;
        Buttons = down ? (byte)(Buttons | mask) : (byte)(Buttons & ~mask);
        update = new(Buttons, 0, 0);return true;
    }
    public void Clear() { Buttons = 0; wheelRemainder = horizontalRemainder = 0; }
}

public readonly record struct MouseUpdate(byte Buttons, int Wheel, int HorizontalWheel);
