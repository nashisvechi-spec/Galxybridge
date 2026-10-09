using System.Buffers.Binary;
using System.Text;
using GalaxyBridge.Core;

int checks = 0;
void Check(bool condition, string name)
{ if (!condition) throw new Exception("FAIL: " + name); checks++; }
void Equal(byte[] actual, string hex, string name) => Check(Convert.ToHexString(actual).Equals(hex, StringComparison.OrdinalIgnoreCase), name);
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (Exception ex) when (ex is ArgumentException or FormatException) { rejected = true; }
    Check(rejected, name);
}

Equal(ControlProtocol.CreateHid(1, "KB", new byte[] { 0xAA, 0xBB }), "0C000100000000024B420002AABB", "create HID wire format includes vendor/product");
Equal(ControlProtocol.HidInput(2, new byte[] { 1, 0xFF, 0x7F, 0 }), "0D0002000401FF7F00", "HID input big endian lengths, raw report");
Equal(ControlProtocol.DestroyHid(1), "0E0001", "destroy HID");
Equal(ControlProtocol.Keycode(3, false), "0001000000030000000000000000", "Android home key-up");
Equal(ControlProtocol.Clipboard("Hi", true), "09000000000000000001000000024869", "clipboard sequence, paste, length");
byte[] russian = ControlProtocol.Clipboard("Привет 🌍", true);
Check(BinaryPrimitives.ReadUInt32BigEndian(russian.AsSpan(10)) == Encoding.UTF8.GetByteCount("Привет 🌍"), "UTF-8 length instead of UTF-16 characters");
Check(Encoding.UTF8.GetString(russian, 14, russian.Length - 14) == "Привет 🌍", "Unicode clipboard survives encoding");
Reject(() => ControlProtocol.Clipboard(new string('Я', ControlProtocol.MaxClipboardBytes), false), "reject oversized UTF-8 clipboard");
Reject(() => ControlProtocol.CreateHid(1, new string('Я', 64), new byte[] { 1 }), "HID name limited in UTF-8 bytes");

KeyboardState keyboard = new();
keyboard.Set(0xE0, true); keyboard.Set(4, true);
Equal(keyboard.Report(), "0100040000000000", "Ctrl + A");
for (int i = 0; i < 100; i++) keyboard.Set(4, true);
Check(keyboard.Report().Count(b => b == 4) == 1, "autorepeat does not accumulate held keys");
keyboard.Set(0xE4, true); keyboard.Set(0xE0, false);
Check(keyboard.Report()[0] == 0x10, "right Ctrl remains after left Ctrl released");
keyboard.Clear();
for (byte key = 4; key <= 10; key++) keyboard.Set(key, true);
Check(keyboard.Report().AsSpan(2).ToArray().All(b => b == 1), "seven keys produce HID rollover error");
keyboard.Set(10, false);
Check(keyboard.Report().AsSpan(2).ToArray().SequenceEqual(new byte[] { 4,5,6,7,8,9 }), "release recovers from rollover");
keyboard.Clear(); Equal(keyboard.Report(), "0000000000000000", "emergency clear releases all modifiers and keys");
Check(ScanCodes.ToUsage(0x1E, false) == 4, "physical A key independent of Windows layout");
Check(ScanCodes.ToUsage(0x1D, false) == 0xE0 && ScanCodes.ToUsage(0x1D, true) == 0xE4, "distinguish left/right Ctrl");
Check(ScanCodes.ToUsage(0x58, false) == 0x45, "F12 hotkey mapping");
Check(ScanCodes.ToUsage(0x1C, true) == 0x58 && ScanCodes.ToUsage(0x1C, false) == 0x28, "keypad Enter vs Enter");
Check(ScanCodes.ToUsage(999, false) == 0, "unsupported key ignored");

Random random = new(240);
for (int i = 0; i < 1000; i++)
{
    int x = random.Next(-2000, 2001), y = random.Next(-2000, 2001), w = random.Next(-300, 301);
    byte button = (byte)random.Next(0, 8);
    byte[][] reports = Hid.MouseReports(button, x, y, w).ToArray();
    Check(reports.Sum(r => (sbyte)r[1]) == x && reports.Sum(r => (sbyte)r[2]) == y && reports.Sum(r => (sbyte)r[3]) == w, "mouse split preserves motion " + i);
    Check(reports.All(r => r.Length == 4 && r[0] == button && r.Skip(1).All(b => (sbyte)b != -128)), "mouse split preserves held buttons " + i);
}
Equal(Hid.MouseReports(0, 0, 0).Single(), "00000000", "zero mouse report releases buttons");
Reject(() => Hid.MouseReports(8, 1, 2).ToArray(), "invalid mouse buttons rejected");

Check(AdbEndpoint.Parse(" 192.168.1.10:37121 ").ToString() == "192.168.1.10:37121", "Wi-Fi IPv4 endpoint");
Check(AdbEndpoint.Parse("[fd00::1]:2222").ToString() == "[fd00::1]:2222", "Wi-Fi IPv6 endpoint");
foreach (string endpoint in new[] { "192.168.0.1:0", "192.168.0.1:65536", "1.2.3.4:5 & whoami", "https://example.com", "host:5555" })
    Reject(() => AdbEndpoint.Parse(endpoint), "reject invalid endpoint " + endpoint);
IReadOnlyList<AdbDevice> devices = AdbDevice.Parse("* daemon started successfully\nList of devices attached\nS25 device product:x model:SM_S931B transport_id:1\nS24 unauthorized\n192.168.1.10:3333 offline\n");
Check(devices.Count == 3 && devices[0].Ready && !devices[1].Ready && !devices[2].Ready, "parse authorized/offline/unauthorized separately");
Check(devices[0].Model == "SM S931B", "parse model");
PhoneIdentity identity = PhoneIdentity.ParseProperties("[ro.serialno]: [S25SERIAL]\n[persist.adb.wifi.guid]: [wifi-guid]\n[other]: [ignored]\n");
Check(identity == new PhoneIdentity("S25SERIAL", "wifi-guid"), "identity from stable device properties");
Check(PhoneIdentity.ParseProperties("[ro.serialno]: []\n[ro.boot.serialno]: [BOOT25]").HardwareSerial == "BOOT25", "boot serial fallback");
Check(!PhoneIdentity.ParseProperties("[ro.serialno]: [unknown]").Known, "unknown identity cannot auto-connect");
Check(!new PhoneIdentity("", "").Matches(new("OTHER", "")), "empty identities do not match");
Check(identity.Matches(new("S25SERIAL", "new-guid")), "hardware identity survives new Wi-Fi guid");
Check(!identity.Matches(new("OTHER", "wifi-guid")), "different hardware rejected even if guid matches");
Check(new PhoneIdentity("", "wifi-guid").Matches(identity), "guid fallback when hardware property is absent");
Check(!new RememberedPhone(new PhoneIdentity(null!, "wifi-guid"), "S25", "serial", "").Valid, "corrupt null identity property rejected");
Check(!new RememberedPhone(null!, "S25", "serial", "").Valid, "corrupt null identity rejected");
IReadOnlyList<AdbMdnsService> mdns = AdbMdnsService.Parse(
    "List of discovered mdns services\n"
    + "adb-wifi-guid-random _adb-tls-connect._tcp. 192.168.0.23:38001\n"
    + "studio-AbCd123456 _adb-tls-pairing._tcp 192.168.0.23:37001\n"
    + "adb-wifi-guidX-random _adb-tls-connect._tcp 192.168.0.24:38002\n"
    + "legacy _adb._tcp 192.168.0.23:5555\n"
    + "bad _adb-tls-connect._tcp host:5555\n"
    + "bad _adb-tls-connect._tcp 192.168.0.23:0\n");
Check(mdns.Count == 3 && mdns[0].Endpoint.Port == 38001, "parse current TLS endpoints and reject legacy/malformed services");
Check(mdns[0].Matches(identity) && !mdns[2].Matches(identity), "guid prefix must end at a delimiter");
Check(new AdbMdnsService("adb-S25SERIAL-Ab1234", "_adb-tls-connect._tcp", AdbEndpoint.Parse("192.168.0.23:38002"))
    .Matches(new("", "adb-S25SERIAL-Ab1234")), "AOSP persistent guid is already prefixed and can be the exact service name");
Check(new AdbMdnsService("adb-S25SERIAL-newSuffix", "_adb-tls-connect._tcp", AdbEndpoint.Parse("192.168.0.23:38002"))
    .Matches(identity), "hardware serial finds a newly advertised TLS port");
Check(mdns[0].OwnsTransport("adb-wifi-guid-random._adb-tls-connect._tcp."), "mDNS transport trailing dot");
Check(mdns[0].OwnsTransport("192.168.0.23:38001") && !mdns[0].OwnsTransport("192.168.0.24:38001"), "bind connection to exact endpoint");
AdbQrPairing qrPair = new("studio-AbCd123456", "0123456789ABCDEF0123456789ABCDEF");
Check(qrPair.Payload == "WIFI:T:ADB;S:studio-AbCd123456;P:0123456789ABCDEF0123456789ABCDEF;;", "Android QR payload with final separator");
Check(qrPair.Matches(mdns[1]) && !qrPair.Matches(mdns[0]), "only this QR pairing service is accepted");
Check(!qrPair.ToString().Contains(qrPair.Secret, StringComparison.Ordinal), "QR credentials are redacted from ToString");
Reject(() => _ = new AdbQrPairing("studio-Ab;d123456", qrPair.Secret).Payload, "reject QR delimiter injection");
Reject(() => _ = new AdbQrPairing(qrPair.ServiceName, "123456").Payload, "QR secret length");
ConnectionRecovery recovery = new();
bool Due(long now = 0, bool auto = true, bool restore = true, bool suspended = false, bool attached = false, bool busy = false) =>
    recovery.Due(auto, restore, suspended, attached, busy, now);
Check(Due(), "startup auto connection ready");
Check(!Due(auto: false) && !Due(suspended: true) && !Due(attached: true) && !Due(busy: true), "startup gates prevent conflicting operations");
recovery.Failed(1000);
Check(recovery.NextAttemptAt == 3000 && !Due(2999) && Due(3000), "first retry after two seconds");
long retryNow = 3000;
foreach (int delay in new[] { 4000, 8000, 16000, 30000, 30000 })
{
    recovery.Failed(retryNow);
    Check(recovery.NextAttemptAt == retryNow + delay, "bounded exponential retry " + delay);
    retryNow = recovery.NextAttemptAt;
}
recovery.Pause();
Check(!Due(retryNow + 999999), "manual disconnect does not reconnect");
recovery.Lost(retryNow);
Check(!Due(retryNow + 1000), "late connection-loss event cannot undo manual pause");
recovery.Resume(retryNow);
Check(Due(retryNow, auto: false, restore: true), "recovery option works independently from startup option");
Check(!Due(retryNow, restore: false), "startup option cannot override disabled recovery");
recovery.Succeeded();
Check(!recovery.Recovering && !recovery.Paused && recovery.Failures == 0, "successful connection resets recovery schedule");
recovery.Lost(10000);
Check(!Due(10999) && Due(11000), "brief pause after loss");
recovery.Wake(10001);
Check(Due(10001) && !Due(10001, suspended: true), "wake retries only after desktop is available");

DesktopBounds bounds = new(-1920, -100, 1920, 1080);
Check(EdgePolicy.AtEdge(bounds, -1, 0, PhoneSide.Right), "edge on negative monitor coordinates");
Check(!EdgePolicy.AtEdge(bounds, -1, -101, PhoneSide.Right), "not outside vertical extent");
Check(EdgePolicy.AtEdge(bounds, -1920, 0, PhoneSide.Left), "left edge");
Check(EdgePolicy.AtEdge(bounds, -20, -100, PhoneSide.Top), "top edge");
Check(EdgePolicy.AtEdge(bounds, -20, 979, PhoneSide.Bottom), "bottom edge");

Check(PhoneEdgeSample.TryParse("GB_EDGE 7 12 1080 2400 0.5 1200 0", 1000, out PhoneEdgeSample? parsedEdge), "parse native phone edge feedback");
Check(parsedEdge is { CaptureId: 7, Sequence: 12, X: .5, ReceivedAt: 1000 }, "preserve epoch, sequence and fractional coordinates");
foreach (string bad in new[] {
    "GB_EDGE 0 1 1080 2400 0 10 0", "GB_EDGE 1 0 1080 2400 0 10 0",
    "GB_EDGE 1 1 0 2400 0 10 0", "GB_EDGE 1 1 1080 2400 -1 10 0",
    "GB_EDGE 1 1 1080 2400 1080 10 0", "GB_EDGE 1 1 1080 2400 0 2400 0",
    "GB_EDGE 1 1 1080 2400 NaN 10 0", "GB_EDGE 1 1 1080 2400 Infinity 10 0",
    "GB_EDGE 1 1 1080 2400 0,5 10 0", "GB_EDGE 1 1 1080 2400 0 10 8",
    "GB_EDGE 1 1 1080 2400 0 10", "GB_EDGE_READY 1" })
    Check(!PhoneEdgeSample.TryParse(bad, 1000, out _), "reject malformed edge sample " + bad);

PhoneEdgeSample atLeft = new(7, 1, 1080, 2400, 0, 1200, 0, 1000);
bool MayReturn(PhoneEdgeSample sample, PhoneSide side = PhoneSide.Right, int dx = -5, int dy = 0,
    int epoch = 7, byte buttons = 0, bool held = false, long entered = 500, long moved = 990, long now = 1010) =>
    EdgeReturnPolicy.CanReturn(sample, epoch, side, dx, dy, buttons, held, entered, moved, now);
Check(MayReturn(atLeft), "phone on right returns at its left edge moving toward PC");
Check(!MayReturn(atLeft, dx: 5), "moving into phone must not return");
Check(!MayReturn(atLeft, dx: 0, dy: 5), "moving along edge must not return");
Check(!MayReturn(atLeft with { X = 10 }), "off-edge cursor must not return");
Check(!MayReturn(atLeft, epoch: 8), "previous capture cannot trigger new capture return");
Check(!MayReturn(atLeft, buttons: 1), "host drag cannot return");
Check(!MayReturn(atLeft with { Buttons = 1 }), "phone drag cannot return");
Check(!MayReturn(atLeft, held: true), "held keyboard key cannot return");
Check(!MayReturn(atLeft, entered: 900), "capture warmup avoids immediate bounce");
Check(!MayReturn(atLeft with { ReceivedAt = 700 }), "stale feedback cannot return");
Check(!MayReturn(atLeft, moved: 700), "stale direction cannot return");
Check(!MayReturn(atLeft with { ReceivedAt = 1100 }), "future-dated feedback rejected");
Check(!MayReturn(atLeft, moved: 1100), "future-dated motion rejected");
Check(MayReturn(atLeft with { X = 1079 }, PhoneSide.Left, dx: 5), "phone on left returns at right edge");
Check(MayReturn(atLeft with { X = 500, Y = 2399 }, PhoneSide.Top, dx: 0, dy: 5), "phone above returns at bottom edge");
Check(MayReturn(atLeft with { X = 500, Y = 0 }, PhoneSide.Bottom, dx: 0, dy: -5), "phone below returns at top edge");
Check(MayReturn(atLeft with { Width = 2400, Height = 1080, Y = 500 }), "landscape phone edge");
Check(PhoneEdgeSample.TryParse("GB_EDGE2 7 1 1080 2400 500 2298 0 0 2296 1080 4", 1000, out PhoneEdgeSample? insetEdge), "parse actual strip inside navigation bar inset");
Check(insetEdge is not null && MayReturn(insetEdge, PhoneSide.Top, dx: 0, dy: 5), "return at actual inset boundary, not guessed full-display bottom");
Check(insetEdge is not null && !MayReturn(insetEdge with { Y = 2295 }, PhoneSide.Top, dx: 0, dy: 5), "reject cursor outside actual strip");
Check(insetEdge is not null && !MayReturn(insetEdge, PhoneSide.Right), "horizontal strip cannot act as vertical edge");
Check(insetEdge is not null && !MayReturn(insetEdge, PhoneSide.Top, dx: 0, dy: -5), "inset edge still requires outward motion");
Check(insetEdge is not null && !MayReturn(insetEdge, PhoneSide.Top, dx: 0, dy: 5, buttons: 1), "inset edge still blocks dragging");
Check(PhoneEdgeSample.TryParse("GB_EDGE2 7 2 1080 2400 20 500 0 20 80 4 2240", 1000, out PhoneEdgeSample? cutoutEdge) &&
    cutoutEdge is not null && MayReturn(cutoutEdge), "return at actual side inset around cutout");
foreach (string badZone in new[] {
    "GB_EDGE2 7 1 1080 2400 0 500 0 -1 0 4 2400",
    "GB_EDGE2 7 1 1080 2400 0 500 0 0 0 0 2400",
    "GB_EDGE2 7 1 1080 2400 0 500 0 1080 0 4 2400",
    "GB_EDGE2 7 1 1080 2400 0 500 0 0 0 1080 2400",
    "GB_EDGE2 7 1 1080 2400 0 500 0 2147483647 0 4 2400" })
    Check(!PhoneEdgeSample.TryParse(badZone, 1000, out _), "reject invalid strip geometry " + badZone);
Check(EdgeReturnPolicy.LaptopPosition(bounds, PhoneSide.Right, atLeft with { Y = 0 }) == (-3, -100), "map top of phone to negative-origin monitor");
Check(EdgeReturnPolicy.LaptopPosition(bounds, PhoneSide.Right, atLeft with { Y = 2399 }) == (-3, 979), "map bottom of phone to monitor");
Check(EdgeReturnPolicy.LaptopPosition(bounds, PhoneSide.Left, atLeft with { Y = 0 }) == (-1918, -100), "return at monitor left edge");
Check(EdgeReturnPolicy.LaptopPosition(bounds, PhoneSide.Top, atLeft with { X = 1079 }) == (-1, -98), "return at monitor top edge");
Check(EdgeReturnPolicy.LaptopPosition(bounds, PhoneSide.Bottom, atLeft with { X = 0 }) == (-1920, 977), "return at monitor bottom edge");

byte[] text = Encoding.UTF8.GetBytes("Текст с телефона 🌍");
using MemoryStream combined = new();
combined.Write(new byte[] { 1,0,0,0,0,0,0,0,1 }); // ack
combined.Write(new byte[] { 2,0,1,0,1,3 }); // HID keyboard LED output
combined.WriteByte(0); byte[] length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)text.Length);
combined.Write(length); combined.Write(text); combined.Position = 0;
using FragmentedStream fragmented = new(combined);
Check((await ControlProtocol.ReadDeviceMessageAsync(fragmented, default)).Type == 1, "fragmented acknowledgement read");
Check((await ControlProtocol.ReadDeviceMessageAsync(fragmented, default)).Type == 2, "HID LED output consumed");
Check((await ControlProtocol.ReadDeviceMessageAsync(fragmented, default)).ClipboardText == "Текст с телефона 🌍", "clipboard after HID output stays aligned");
using MemoryStream invalid = new(new byte[] { 0,255,255,255,255 });
bool invalidLength = false;
try { _ = await ControlProtocol.ReadDeviceMessageAsync(invalid, default); } catch (InvalidDataException) { invalidLength = true; }
Check(invalidLength, "untrusted incoming length rejected before allocation");
using MemoryStream truncated = new(new byte[] { 0,0,0 });
bool eof = false;
try { _ = await ControlProtocol.ReadDeviceMessageAsync(truncated, default); } catch (EndOfStreamException) { eof = true; }
Check(eof, "disconnect during header surfaces as EOF");
Console.WriteLine($"PASS: {checks} core assertions");

sealed class FragmentedStream(Stream inner) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(1, count));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
