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
