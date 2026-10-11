namespace GalaxyBridge.Core;

public enum PhoneSide { Right, Left, Top, Bottom }
public readonly record struct DesktopBounds(int X, int Y, int Width, int Height);
public static class EdgePolicy
{
    public static bool AtEdge(DesktopBounds b, int x, int y, PhoneSide side) => side switch
    {
        PhoneSide.Right => x == b.X + b.Width - 1 && y >= b.Y && y < b.Y + b.Height,
        PhoneSide.Left => x == b.X && y >= b.Y && y < b.Y + b.Height,
        PhoneSide.Top => y == b.Y && x >= b.X && x < b.X + b.Width,
        PhoneSide.Bottom => y == b.Y + b.Height - 1 && x >= b.X && x < b.X + b.Width,
        _ => false
    };
}
