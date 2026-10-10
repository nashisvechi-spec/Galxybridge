using GalaxyBridge.Core;
namespace GalaxyBridge.Windows;
internal interface IPhoneControl
{
    bool IsAlive { get; }
    PhoneEdgeSample? LatestEdge { get; }
    int BeginEdgeReturn(PhoneSide side);
    void EndEdgeReturn();
    bool Send(byte[] packet);
    void Keyboard(byte[] report);
    void Mouse(byte buttons, int dx, int dy, int wheel = 0, int horizontalWheel = 0);
    void ReleaseInputs();
}
