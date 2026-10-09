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
DesktopBounds bounds = new(-1920, -100, 1920, 1080);
Check(EdgePolicy.AtEdge(bounds, -1, 0, PhoneSide.Right), "edge on negative monitor coordinates");
Check(!EdgePolicy.AtEdge(bounds, -1, -101, PhoneSide.Right), "not outside vertical extent");
Check(EdgePolicy.AtEdge(bounds, -1920, 0, PhoneSide.Left), "left edge");
Check(EdgePolicy.AtEdge(bounds, -20, -100, PhoneSide.Top), "top edge");
Check(EdgePolicy.AtEdge(bounds, -20, 979, PhoneSide.Bottom), "bottom edge");

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
