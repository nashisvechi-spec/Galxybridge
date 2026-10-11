using GalaxyBridge.Core;
namespace GalaxyBridge.Windows;
internal interface IPhoneControl
{
    bool IsAlive { get; }
    bool InputReady { get; }
    PhoneEdgeSample? LatestEdge { get; }
    int BeginEdgeReturn(PhoneSide side);
    void EndEdgeReturn();
    bool Paste(string text);
    void Keyboard(byte[] report);
    void Mouse(byte buttons, int dx, int dy, int wheel = 0, int horizontalWheel = 0);
    void ReleaseInputs();
}
