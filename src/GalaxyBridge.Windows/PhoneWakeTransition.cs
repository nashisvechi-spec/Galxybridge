namespace GalaxyBridge.Windows;

// A wake request never owns the PC cursor. Capture waits for actual readiness.
internal sealed class PhoneWakeTransition
{
    public bool Pending { get; private set; }
    public bool FromEdge { get; private set; }
    private long expires;
    public void Begin(bool fromEdge, long now)
    {
        Pending = true; FromEdge = fromEdge; expires = now + 15000;
    }
    public bool ShouldResume(bool allowed, bool ready, bool held, bool atEdge, long now)
    {
        if (!allowed || now >= expires || FromEdge && !atEdge) Cancel();
        return Pending && ready && !held;
    }
    public void Cancel() => Pending = false;
}
