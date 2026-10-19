using System.Drawing;

namespace GalaxyBridge.Windows;

internal enum FileWidgetAction { None, Send, Close, Cancel, Move }

// Drawing and hit targets share logical coordinates, including after DPI changes.
internal static class FileWidgetLayout
{
    public const int ArtWidth = 320, ArtHeight = 184;
    public static readonly RectangleF SendBounds = new(12, 52, 296, 120);
    public static readonly RectangleF CloseBounds = new(276, 10, 32, 32);
    public static readonly RectangleF CancelBounds = new(244, 136, 60, 28);
    public static Size SizeForDpi(int dpi, Size available)
    {
        if (available.Width <= 0 || available.Height <= 0) return Size.Empty;
        double scale = Math.Min((dpi > 0 ? dpi : 96) / 96d, Math.Min(available.Width / (double)ArtWidth, available.Height / (double)ArtHeight));
        return new Size(Math.Max(1, (int)Math.Round(ArtWidth * scale)), Math.Max(1, (int)Math.Round(ArtHeight * scale)));
    }
    public static RectangleF Viewport(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return RectangleF.Empty;
        float scale = Math.Min(size.Width / (float)ArtWidth, size.Height / (float)ArtHeight);
        float width = ArtWidth * scale, height = ArtHeight * scale;
        return new RectangleF((size.Width - width) / 2, (size.Height - height) / 2, width, height);
    }
    public static FileWidgetAction Hit(Point point, Size size, bool busy)
    {
        if (size.Width <= 0 || size.Height <= 0 || point.X < 0 || point.Y < 0 || point.X >= size.Width || point.Y >= size.Height)
            return FileWidgetAction.None;
        RectangleF viewport = Viewport(size);
        if (!viewport.Contains(point)) return FileWidgetAction.None;
        float scale = viewport.Width / ArtWidth;
        float x = (point.X - viewport.X) / scale, y = (point.Y - viewport.Y) / scale;
        float dx = (x - CloseBounds.X - CloseBounds.Width / 2) / (CloseBounds.Width / 2);
        float dy = (y - CloseBounds.Y - CloseBounds.Height / 2) / (CloseBounds.Height / 2);
        if (dx * dx + dy * dy <= 1) return FileWidgetAction.Close;
        if (busy && CancelBounds.Contains(x, y)) return FileWidgetAction.Cancel;
        return SendBounds.Contains(x, y) ? FileWidgetAction.Send : FileWidgetAction.Move;
    }
    public static bool CanDrop(bool canSend, bool busy, string[]? paths) => canSend && !busy &&
        paths is { Length: > 0 and <= 100 } && paths.All(File.Exists);
    public static Rectangle Bounds(FileWidgetAction action, Size size)
    {
        RectangleF b = action switch { FileWidgetAction.Close => CloseBounds, FileWidgetAction.Cancel => CancelBounds, _ => SendBounds };
        RectangleF viewport = Viewport(size);float scale = viewport.Width / ArtWidth;
        return Rectangle.Round(new RectangleF(viewport.X + b.X * scale, viewport.Y + b.Y * scale, b.Width * scale, b.Height * scale));
    }
}
