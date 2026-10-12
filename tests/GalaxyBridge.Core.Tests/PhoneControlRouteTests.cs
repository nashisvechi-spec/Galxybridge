using GalaxyBridge.Core;
using GalaxyBridge.Windows;

static class PhoneControlRouteTests
{
    public static void Run(Action<bool, string> check)
    {
        FakeControl wifi = new() { InputReady = true }, bluetooth = new();
        List<IPhoneControl?> released = [];
        PhoneControlRoute? route = null;
        route = new(() => { released.Add(route?.Current); route?.Current?.ReleaseInputs(); });
        check(!route.Update(null, null) && released.Count == 0, "no input connections means no route or release");
        check(route.Update(bluetooth, wifi) && route.Current == wifi, "a paired but unready Bluetooth connection cannot block Wi-Fi");
        check(!route.Update(bluetooth, wifi) && wifi.Releases == 0, "unchanged Wi-Fi does not interrupt input");
        bluetooth.InputReady = true;
        check(route.Update(bluetooth, wifi) && route.Current == bluetooth, "ready Bluetooth takes priority over ready Wi-Fi");
        check(released[^1] == wifi && wifi.Releases == 1 && bluetooth.Releases == 0, "Wi-Fi releases before Bluetooth becomes the target");
        check(!route.Update(bluetooth, null), "losing Wi-Fi does not interrupt the Bluetooth input route");
        bluetooth.InputReady = false;
        check(route.Update(bluetooth, wifi) && route.Current == wifi, "Bluetooth disconnect or suspend restores available Wi-Fi");
        check(released[^1] == bluetooth && bluetooth.Releases == 1, "old Bluetooth buttons release before touch fallback");
        wifi.InputReady = false;
        check(route.Update(bluetooth, wifi) && route.Current is null, "unready accessibility and Bluetooth disable capture");
        bluetooth.InputReady = true;
        check(route.Update(bluetooth, wifi) && route.Current == bluetooth, "Bluetooth input works without accessibility readiness");
        wifi.InputReady = true;
        check(!route.Update(bluetooth, wifi), "Wi-Fi recovery keeps the ready system mouse selected");
        check(route.Update(null, wifi) && route.Current == wifi, "disabling Bluetooth returns to the app cursor");
        check(bluetooth.Releases == 2 && wifi.Releases == 2, "each old transport releases exactly on route changes");
        route.Update(bluetooth, wifi);
        check(route.Update(bluetooth, wifi, preferWifi: true) && route.Current == wifi,"external DeX target uses display-specific Wi-Fi even with ready Bluetooth");
        check(!route.Update(bluetooth, wifi, preferWifi: true),"stable DeX route does not interrupt movement");
        wifi.InputReady = false;
        check(route.Update(bluetooth, wifi, preferWifi: true) && route.Current is null,"missing DeX/accessibility cannot silently fall back to phone Bluetooth cursor");
        wifi.InputReady = true;
        check(route.Update(bluetooth, wifi, preferWifi: true) && route.Current == wifi,"DeX readiness restores explicit Wi-Fi input");
        check(route.Update(bluetooth, wifi) && route.Current == bluetooth,"phone mode restores original ready Bluetooth priority");
    }
    private sealed class FakeControl : IPhoneControl
    {
        public bool InputReady { get; set; }
        public bool IsAlive => true; // Pairing alone does not establish input readiness.
        public int Releases { get; private set; }
        public PhoneEdgeSample? LatestEdge => null;
        public int BeginEdgeReturn(PhoneSide side) => 0;
        public void EndEdgeReturn() { }
        public bool Paste(string text) => true;
        public void Keyboard(byte[] report) { }
        public void Mouse(byte buttons, int dx, int dy, int wheel = 0, int horizontalWheel = 0) { }
        public void ReleaseInputs() => Releases++;
    }
}
