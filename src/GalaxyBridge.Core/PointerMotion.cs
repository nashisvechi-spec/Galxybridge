namespace GalaxyBridge.Core;

// Batch high-rate hook events without waiting for the Windows UI timer.
// A forced read preserves the ordering before buttons and keyboard modifiers.
public sealed class PointerMotion
{
    private double x, y, sensitivity = 1;
    private long? sentAt;
    public void Reset(double speed = 1)
    { x = y = 0; sentAt = null; sensitivity = double.IsFinite(speed) ? Math.Clamp(speed, .25, 4) : 1; }
    public void Add(int dx, int dy) { x += dx * sensitivity; y += dy * sensitivity; }
    public bool TryTake(long now, bool force, out int dx, out int dy)
    {
        dx = dy = 0;
        if (!force && sentAt is long sent && now >= sent && now - sent < 8) return false;
        dx = (int)Math.Clamp(Math.Truncate(x), -8192, 8192);
        dy = (int)Math.Clamp(Math.Truncate(y), -8192, 8192);
        if (dx == 0 && dy == 0) return false;
        x -= dx; y -= dy; sentAt = now; return true;
    }
}
