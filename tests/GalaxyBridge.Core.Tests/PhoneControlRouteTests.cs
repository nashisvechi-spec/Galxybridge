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
        DisplayCaptureSwitch migration = new();
        migration.Changed(false,true,1000);
        check(!migration.ShouldResume(true,true,false,1001),"DeX connection selects the target without stealing an idle PC cursor");
        migration.Changed(true,true,2000);
        check(migration.Pending && !migration.ShouldResume(true,false,false,2001),"active phone capture waits for DeX readiness after releasing old input");
        check(!migration.ShouldResume(true,true,true,2002),"held mouse or keyboard input is never replayed onto DeX");
        check(migration.ShouldResume(true,true,false,2003),"active control resumes automatically on DeX once ready and released");
        migration.Cancel();
        check(!migration.Pending,"successful migration or explicit user stop clears automatic resume");
        migration.Changed(true,false,3000);
        check(!migration.Pending,"input failures and geometry-only revisions do not trigger retry loops");
        migration.Changed(true,true,4000);migration.Changed(false,false,4001);
        check(migration.ShouldResume(true,true,false,4002),"monitor wake can finish an already pending automatic migration");
        check(!migration.ShouldResume(false,true,false,4003) && !migration.Pending,"disconnect pause suspend or file sending cancels pending migration");
        migration.Changed(true,true,5000);
        check(!migration.ShouldResume(true,true,false,20001) && !migration.Pending,"unavailable monitor cannot capture the PC cursor long after hotplug");
        PhoneWakeTransition wake = new();
        wake.Begin(true,1000);
        check(!wake.ShouldResume(true,false,false,true,1100) && wake.Pending,"sleeping or locked phone keeps PC cursor while wake is pending");
        check(!wake.ShouldResume(true,true,true,true,1200) && wake.Pending,"waking never replays held mouse or keyboard input");
        check(wake.ShouldResume(true,true,false,true,1300),"unlocked awake input target permits requested edge transition");
        wake.Cancel();check(!wake.Pending,"successful wake transition consumes pending intent");
        wake.Begin(true,2000);
        check(!wake.ShouldResume(true,false,false,false,2100) && !wake.Pending,"leaving PC edge cancels wake intent before later unlock");
        check(!wake.ShouldResume(true,true,false,true,2200),"returning to edge cannot revive cancelled wake intent");
        wake.Begin(false,3000);
        check(wake.ShouldResume(true,true,false,false,3100),"button and hotkey wake do not require cursor at edge");
        check(!wake.ShouldResume(false,true,false,true,3200) && !wake.Pending,"pause disconnect sending or DeX target change cancels wake intent");
        wake.Begin(true,4000);
        check(!wake.ShouldResume(true,true,false,true,19000) && !wake.Pending,"wake expires at 15 seconds without delayed cursor capture");
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
