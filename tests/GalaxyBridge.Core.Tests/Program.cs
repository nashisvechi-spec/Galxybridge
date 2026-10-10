using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
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
    int x = random.Next(-2000, 2001), y = random.Next(-2000, 2001), w = random.Next(-300, 301), h = random.Next(-300, 301);
    byte button = (byte)random.Next(0, 32);
    byte[][] reports = Hid.MouseReports(button, x, y, w, h).ToArray();
    Check(reports.Sum(r => (sbyte)r[1]) == x && reports.Sum(r => (sbyte)r[2]) == y && reports.Sum(r => (sbyte)r[3]) == w && reports.Sum(r => (sbyte)r[4]) == h, "mouse split preserves both motion and wheel axes " + i);
    Check(reports.All(r => r.Length == Hid.MouseReportLength && r[0] == button && r.Skip(1).All(b => (sbyte)b != -128)), "mouse split preserves all five held buttons " + i);
}
Equal(Hid.MouseReports(0, 0, 0).Single(), "0000000000", "zero mouse report releases all five buttons and both wheels");
Reject(() => Hid.MouseReports(32, 1, 2).ToArray(), "undefined sixth mouse button rejected");
await NativeMouseTests.RunAsync(Check);

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
Check(qrPair.Matches(mdns[1] with { Instance = qrPair.ServiceName.ToLowerInvariant() }), "DNS names match independent of ASCII case");
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
Check(FileTransfer.Root == "/sdcard/Download/GalaxyBridge", "all uploads share one folder");
Check(FileTransfer.AllocateNames(["photo.jpg", "PHOTO.JPG"], ["photo.jpg", "photo (2).jpg"])
    .SequenceEqual(new[] { "photo (3).jpg", "PHOTO (4).JPG" }), "existing files and directories reserve their names across batches");
Check(FileTransfer.ParseExistingNames(FileTransfer.Root + "/a\nline.txt\0" + FileTransfer.Root + "/folder\0" +
    "/other/file\0" + FileTransfer.Root + "/nested/file\0").SequenceEqual(new[] { "a\nline.txt", "folder" }),
    "NUL listing preserves names containing newlines and excludes other paths");
string longName = new string('Я', 125) + ".png";
string shortenedName = FileTransfer.AllocateNames([longName], [longName])[0];
Check(shortenedName.EndsWith(" (2).png", StringComparison.Ordinal) && Encoding.UTF8.GetByteCount(shortenedName) <= 255,
    "Unicode stem is shortened on rune boundary when a suffix is needed");
string[] uploadNames = FileTransfer.AllocateNames(["photo.jpg", "photo.jpg", "PHOTO.JPG", "photo (2).jpg", "Икона 🕯.png", "README", "README"]);
Check(uploadNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 7, "equal names never overwrite in one batch");
Check(uploadNames[0] == "photo.jpg" && uploadNames[1] == "photo (2).jpg" && uploadNames[2] == "PHOTO (3).JPG", "duplicates get suffix before extension");
Check(uploadNames[4] == "Икона 🕯.png" && uploadNames[6] == "README (2)", "Unicode and extensionless names survive allocation");
Reject(() => FileTransfer.AllocateNames(["../file.txt"]), "reject remote parent path");
Reject(() => FileTransfer.AllocateNames(["folder\\file.txt"]), "reject Windows separators in remote leaf names");
Reject(() => FileTransfer.AllocateNames([".."]), "reject parent directory leaf");
Reject(() => FileTransfer.AllocateNames(["line\nfile.txt"]), "reject newline in file names");
Reject(() => FileTransfer.AllocateNames([new string('Я', 128)]), "enforce phone filename limit in UTF-8 bytes");
Check(FileTransfer.ShellQuote("O'Brien $(touch marker);.txt") == "'O'\"'\"'Brien $(touch marker);.txt'", "shell metacharacters remain in quoted filename");
Check(FileTransfer.ShellQuote("a`b.txt") == "'a`b.txt'", "backticks stay literal in shell quoting");
Reject(() => FileTransfer.ShellQuote("file\0name"), "reject NUL in shell argument");

Check(AdbMdnsService.Parse("studio-AbCd123456._adb-tls-pairing._tcp.local. _adb-tls-pairing._tcp. 192.168.0.23:37001")[0].Instance == qrPair.ServiceName,
    "normalize fully qualified pairing service names before exact QR matching");
byte[] dnsQuery = MdnsPacket.Query("studio-AbCd123456._adb-tls-pairing._tcp.local", 33, 0x1234);
Check(BinaryPrimitives.ReadUInt16BigEndian(dnsQuery) == 0x1234 && BinaryPrimitives.ReadUInt16BigEndian(dnsQuery.AsSpan(4)) == 1,
    "direct mDNS query has transaction ID and one question");
Equal(dnsQuery[^4..], "00210001", "legacy mDNS query asks for SRV in IN class");
Reject(() => MdnsPacket.Query(new string('a', 64) + ".local", 33, 0), "reject DNS label longer than 63 bytes");
Reject(() => MdnsPacket.Query("host..local", 1, 0), "reject empty interior DNS label");
using MemoryStream dnsResponse = new();
dnsResponse.Write(dnsQuery);
byte[] dnsNamePointer = [0xC0, 0x0C];
dnsResponse.Write(dnsNamePointer); // owner is the query name
byte[] dnsHost = [7, (byte)'a', (byte)'n', (byte)'d', (byte)'r', (byte)'o', (byte)'i', (byte)'d', 5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0];
byte[] srvHeader = new byte[10];
BinaryPrimitives.WriteUInt16BigEndian(srvHeader, 33); BinaryPrimitives.WriteUInt16BigEndian(srvHeader.AsSpan(2), 0x8001);
BinaryPrimitives.WriteUInt32BigEndian(srvHeader.AsSpan(4), 120);
BinaryPrimitives.WriteUInt16BigEndian(srvHeader.AsSpan(8), (ushort)(6 + dnsHost.Length));
dnsResponse.Write(srvHeader);
byte[] srvData = new byte[6]; BinaryPrimitives.WriteUInt16BigEndian(srvData.AsSpan(4), 37001); dnsResponse.Write(srvData);
int hostOffset = (int)dnsResponse.Position; dnsResponse.Write(dnsHost);
dnsResponse.Write(new byte[] { (byte)(0xC0 | (hostOffset >> 8)), (byte)hostOffset });
byte[] aHeader = new byte[10]; BinaryPrimitives.WriteUInt16BigEndian(aHeader, 1);
BinaryPrimitives.WriteUInt16BigEndian(aHeader.AsSpan(2), 0x8001); BinaryPrimitives.WriteUInt32BigEndian(aHeader.AsSpan(4), 120);
BinaryPrimitives.WriteUInt16BigEndian(aHeader.AsSpan(8), 4); dnsResponse.Write(aHeader); dnsResponse.Write(new byte[] { 192, 168, 0, 23 });
byte[] dnsPacket = dnsResponse.ToArray();
BinaryPrimitives.WriteUInt16BigEndian(dnsPacket.AsSpan(2), 0x8400);
BinaryPrimitives.WriteUInt16BigEndian(dnsPacket.AsSpan(6), 1); BinaryPrimitives.WriteUInt16BigEndian(dnsPacket.AsSpan(10), 1);
IReadOnlyList<MdnsRecord> dnsRecords = MdnsPacket.Parse(dnsPacket, 0x1234);
Check(dnsRecords.Count == 2 && dnsRecords[0].Port == 37001 && dnsRecords[0].Target == "android.local" &&
    dnsRecords[1].Address == "192.168.0.23", "parse compressed SRV owner and A owner across answer/additional sections");
Reject(() => MdnsPacket.Parse(dnsPacket, 0x4321), "reject unrelated legacy response ID");
Reject(() => MdnsPacket.Parse(dnsPacket[..^1], 0x1234), "reject truncated A record");
byte[] loopingDns = (byte[])dnsPacket.Clone(); loopingDns[12] = 0xC0; loopingDns[13] = 12;
Reject(() => MdnsPacket.Parse(loopingDns, 0x1234), "reject cyclic name pointer");
byte[] oversizedDns = (byte[])dnsPacket.Clone(); BinaryPrimitives.WriteUInt16BigEndian(oversizedDns.AsSpan(6), 129);
Reject(() => MdnsPacket.Parse(oversizedDns, 0x1234), "bound record count before reading untrusted packet");
byte[] goodbyeDns = (byte[])dnsPacket.Clone();
BinaryPrimitives.WriteUInt32BigEndian(goodbyeDns.AsSpan(dnsQuery.Length + 6), 0);
Check(MdnsPacket.Parse(goodbyeDns, 0x1234).Count == 1, "ignore goodbye SRV with zero TTL");
Random dnsFuzz = new(25);
for (int i = 0; i < 200; i++)
{
    byte[] arbitraryDns = new byte[dnsFuzz.Next(0, 160)]; dnsFuzz.NextBytes(arbitraryDns);
    if (arbitraryDns.Length >= 12)
    { BinaryPrimitives.WriteUInt16BigEndian(arbitraryDns, 0x1234); BinaryPrimitives.WriteUInt16BigEndian(arbitraryDns.AsSpan(2), 0x8400); }
    try { _ = MdnsPacket.Parse(arbitraryDns, 0x1234); } catch (FormatException) { }
}
Check(true, "arbitrary DNS bytes do not escape bounds or pointer validation");
// Independent Wi-Fi framing: Unicode, fragmentation, bounds and untrusted object validation.
byte[] lanText = LanProtocol.Encode(new { type = "text", text = "Привет 🌍\n" });
Check(BinaryPrimitives.ReadInt32BigEndian(lanText) == lanText.Length - 4, "LAN byte length");
using (var lanInput = new FragmentedStream(new MemoryStream(lanText)))
using (var lanRead = await LanProtocol.ReadAsync(lanInput, CancellationToken.None))
    Check(lanRead.RootElement.GetProperty("text").GetString() == "Привет 🌍\n", "LAN fragmented Unicode frame");
Check(LanProtocol.SafeName("Икона (2).jpg") && !LanProtocol.SafeName("../x") && !LanProtocol.SafeName("x\ny\nz"), "LAN safe file names");
Check(LanProtocol.HexSecret(new string('a', 64)) && !LanProtocol.HexSecret(new string('A', 64)), "LAN strict secret encoding");
Check(LanProtocol.Identifier(new string('a', 32)) && !LanProtocol.Identifier(new string('a', 31)), "LAN identifier bounds");
Reject(() => LanProtocol.Encode(new { text = new string('x', LanProtocol.MaxFrame) }), "LAN excessive outbound frame");
foreach (int lanFrameLength in new[] { -1, 0, 1, LanProtocol.MaxFrame + 1, int.MaxValue })
{
    byte[] header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, lanFrameLength); bool lanFrameRejected = false;
    try { using var result = await LanProtocol.ReadAsync(new MemoryStream(header), CancellationToken.None); }
    catch (InvalidDataException) { lanFrameRejected = true; }
    Check(lanFrameRejected, "LAN reject hostile length before allocation");
}
byte[] arrayFrame = new byte[] { 0, 0, 0, 2, (byte)'[', (byte)']' }; bool arrayRejected = false;
try { using var result = await LanProtocol.ReadAsync(new MemoryStream(arrayFrame), CancellationToken.None); }
catch (InvalidDataException) { arrayRejected = true; }
Check(arrayRejected, "LAN reject non-object frame");

checks += LanFileReceiverTests.Run();
foreach (string clipboardValue in new[] { "Текст с телефона 🌍\n", "https://example.com/?q=икона&n=2", "  text\n", new string('Я', 8000) })
{
    using var clipboardMessage = JsonDocument.Parse(JsonSerializer.Serialize(new { text = clipboardValue }));
    Check(LanProtocol.ClipboardText(clipboardMessage.RootElement) == clipboardValue, "LAN clipboard keeps exact Unicode text and links");
}
foreach (object clipboardValue in new object[] { "", "x\0y", new string('x', 16001), new string('Я', 8001), 123 })
{
    using var clipboardMessage = JsonDocument.Parse(JsonSerializer.Serialize(new { text = clipboardValue }));
    bool rejectedClipboard = false;
    try { LanProtocol.ClipboardText(clipboardMessage.RootElement); } catch (InvalidDataException) { rejectedClipboard = true; }
    Check(rejectedClipboard, "reject invalid LAN clipboard text");
}
using (var missingClipboard = JsonDocument.Parse("{}"))
{
    bool rejectedClipboard = false;
    try { LanProtocol.ClipboardText(missingClipboard.RootElement); } catch (InvalidDataException) { rejectedClipboard = true; }
    Check(rejectedClipboard, "reject missing LAN clipboard text");
}
await LanClipboardTests.RunAsync(Check);
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
