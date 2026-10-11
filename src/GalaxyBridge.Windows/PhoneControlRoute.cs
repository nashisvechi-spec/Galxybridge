namespace GalaxyBridge.Windows;

// Change transports only after releasing the previous capture. Do not replay
// held keys or a drag on the new transport when a connection appears/disappears.
internal sealed class PhoneControlRoute(Action releaseCapture)
{
    public IPhoneControl? Current { get; private set; }
    public bool Update(IPhoneControl? bluetooth, IPhoneControl? wifi)
    {
        IPhoneControl? next = bluetooth?.InputReady == true ? bluetooth : wifi?.InputReady == true ? wifi : null;
        if (ReferenceEquals(Current, next)) return false;
        releaseCapture();
        Current = next;
        return true;
    }
}
