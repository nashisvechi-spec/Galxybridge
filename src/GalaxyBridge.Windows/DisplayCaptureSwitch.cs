namespace GalaxyBridge.Windows;

// Resume only a capture already owned by the app; held input is never replayed.
internal sealed class DisplayCaptureSwitch
{
    public bool Pending { get; private set; }
    private long expires;
    public void Changed(bool active, bool moved, long now)
    {
        if (Pending || active && moved) { Pending = true; expires = now + 15000; }
    }
    public bool ShouldResume(bool allowed, bool ready, bool held, long now)
    {
        if (!allowed || now > expires) Cancel();
        return Pending && ready && !held;
    }
    public void Cancel() => Pending = false;
}
