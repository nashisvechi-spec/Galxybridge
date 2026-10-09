using System.Globalization;

namespace GalaxyBridge.Core;

public sealed record PhoneEdgeSample(int CaptureId, long Sequence, int Width, int Height,
    double X, double Y, byte Buttons, long ReceivedAt)
{
    public static bool TryParse(string line, long now, out PhoneEdgeSample? sample)
    {
        sample = null;
        if (line.Length > 256) return false;
        string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 8 || p[0] != "GB_EDGE" ||
            !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int capture) || capture < 1 ||
            !long.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out long sequence) || sequence < 1 ||
            !int.TryParse(p[3], out int width) || width is < 16 or > 20000 ||
            !int.TryParse(p[4], out int height) || height is < 16 or > 20000 ||
            !double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) || !double.IsFinite(x) ||
            !double.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) || !double.IsFinite(y) ||
            !byte.TryParse(p[7], out byte buttons) || buttons > 7 || x < 0 || y < 0 || x >= width || y >= height)
            return false;
        sample = new(capture, sequence, width, height, x, y, buttons, now);
        return true;
    }
}

public static class EdgeReturnPolicy
{
    public static bool CanReturn(PhoneEdgeSample sample, int capture, PhoneSide side,
        int dx, int dy, byte hostButtons, bool keyHeld, long enteredAt, long motionAt, long now)
    {
        if (sample.CaptureId != capture || capture < 1 || sample.Buttons != 0 || hostButtons != 0 || keyHeld ||
            now - enteredAt < 350 || now < sample.ReceivedAt || now - sample.ReceivedAt > 250 ||
            now < motionAt || now - motionAt > 200) return false;
        return side switch
        {
            PhoneSide.Right => sample.X < 4 && dx < 0,
            PhoneSide.Left => sample.X >= sample.Width - 4 && dx > 0,
            PhoneSide.Top => sample.Y >= sample.Height - 4 && dy > 0,
            PhoneSide.Bottom => sample.Y < 4 && dy < 0,
            _ => false
        };
    }

    public static (int X, int Y) LaptopPosition(DesktopBounds b, PhoneSide side, PhoneEdgeSample sample)
    {
        if (b.Width < 5 || b.Height < 5) throw new ArgumentOutOfRangeException(nameof(b));
        int x = b.X + (int)Math.Round(Math.Clamp(sample.X / (sample.Width - 1), 0, 1) * (b.Width - 1));
        int y = b.Y + (int)Math.Round(Math.Clamp(sample.Y / (sample.Height - 1), 0, 1) * (b.Height - 1));
        return side switch
        {
            PhoneSide.Right => (b.X + b.Width - 3, y),
            PhoneSide.Left => (b.X + 2, y),
            PhoneSide.Top => (x, b.Y + 2),
            PhoneSide.Bottom => (x, b.Y + b.Height - 3),
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }
}
