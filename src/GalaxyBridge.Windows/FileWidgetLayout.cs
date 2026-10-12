using System.Drawing;

namespace GalaxyBridge.Windows;

internal enum FileWidgetAction { None, Send, Close, Cancel, Move }

// Hit targets share the reference PNG coordinates, including after DPI changes.
internal static class FileWidgetLayout
{
    public const int ArtWidth = 1635, ArtHeight = 962;
    public static readonly RectangleF SendBounds = new(150, 220, 1330, 525);
    public static readonly RectangleF CloseBounds = new(1372, 121, 136, 136);
    public static readonly RectangleF CancelBounds = new(1250, 650, 220, 75);
    public static FileWidgetAction Hit(Point point, Size size, bool busy)
    {
        if (size.Width <= 0 || size.Height <= 0 || point.X < 0 || point.Y < 0 || point.X >= size.Width || point.Y >= size.Height)
            return FileWidgetAction.None;
        float x = point.X * (float)ArtWidth / size.Width, y = point.Y * (float)ArtHeight / size.Height;
        float dx = (x - 1440) / 68, dy = (y - 189) / 68;
        if (dx * dx + dy * dy <= 1) return FileWidgetAction.Close;
        if (busy && CancelBounds.Contains(x, y)) return FileWidgetAction.Cancel;
        return SendBounds.Contains(x, y) ? FileWidgetAction.Send : FileWidgetAction.Move;
    }
    public static bool CanDrop(bool canSend, bool busy, string[]? paths) => canSend && !busy &&
        paths is { Length: > 0 and <= 100 } && paths.All(File.Exists);
    public static Rectangle Bounds(FileWidgetAction action, Size size)
    {
        RectangleF b = action switch { FileWidgetAction.Close => CloseBounds, FileWidgetAction.Cancel => CancelBounds, _ => SendBounds };
        return Rectangle.Round(new RectangleF(b.X * size.Width / ArtWidth, b.Y * size.Height / ArtHeight,
            b.Width * size.Width / ArtWidth, b.Height * size.Height / ArtHeight));
    }
}
