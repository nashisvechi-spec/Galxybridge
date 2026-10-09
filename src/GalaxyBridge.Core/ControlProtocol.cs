using System.Buffers.Binary;
using System.Text;

namespace GalaxyBridge.Core;

// Wire format is pinned to scrcpy-server 5.0.1. See THIRD_PARTY_NOTICES.md.
public static class ControlProtocol
{
    public const string ServerVersion = "5.0.1";
    public const ushort KeyboardId = 1;
    public const ushort MouseId = 2;
    public const int MaxClipboardBytes = (1 << 18) - 14;

    public static byte[] CreateHid(ushort id, string name, ReadOnlySpan<byte> descriptor)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(name);
        if (utf8.Length > 127 || descriptor.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(name));
        // type, id, vendor, product, nameLength, name, descriptorLength, descriptor
        byte[] packet = new byte[10 + utf8.Length + descriptor.Length];
        packet[0] = 12;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), id);
        // Vendor/product 0: no impersonation of Samsung hardware.
        packet[7] = (byte)utf8.Length;
        utf8.CopyTo(packet, 8);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(8 + utf8.Length), (ushort)descriptor.Length);
        descriptor.CopyTo(packet.AsSpan(10 + utf8.Length));
        return packet;
    }

    public static byte[] HidInput(ushort id, ReadOnlySpan<byte> report)
    {
        if (report.Length > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(report));
        byte[] packet = new byte[5 + report.Length];
        packet[0] = 13;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), id);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(3), (ushort)report.Length);
        report.CopyTo(packet.AsSpan(5));
        return packet;
    }

    public static byte[] DestroyHid(ushort id)
    {
        byte[] packet = [14, 0, 0];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(1), id);
        return packet;
    }

    public static byte[] Clipboard(string text, bool paste)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        if (utf8.Length > MaxClipboardBytes)
            throw new ArgumentOutOfRangeException(nameof(text), "Текст превышает 256 КБ.");
        byte[] packet = new byte[14 + utf8.Length];
        packet[0] = 9;
        // sequence 0 means no acknowledgement is requested.
        packet[9] = paste ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(10), (uint)utf8.Length);
        utf8.CopyTo(packet, 14);
        return packet;
    }

    public static byte[] Keycode(int androidKeycode, bool down)
    {
        byte[] packet = new byte[14];
        packet[1] = down ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(2), androidKeycode);
        return packet;
    }

    public static async Task<DeviceMessage> ReadDeviceMessageAsync(Stream stream, CancellationToken token)
    {
        byte[] type = new byte[1];
        await stream.ReadExactlyAsync(type, token).ConfigureAwait(false);
        if (type[0] == 0)
        {
            byte[] header = new byte[4];
            await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length > (1 << 18) - 5) throw new InvalidDataException("Invalid clipboard length.");
            byte[] data = new byte[(int)length];
            await stream.ReadExactlyAsync(data, token).ConfigureAwait(false);
            return new DeviceMessage(0, Encoding.UTF8.GetString(data));
        }
        if (type[0] == 1)
        {
            byte[] ack = new byte[8];
            await stream.ReadExactlyAsync(ack, token).ConfigureAwait(false);
            return new DeviceMessage(1, null);
        }
        if (type[0] == 2)
        {
            byte[] header = new byte[4];
            await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
            if (length > 4096) throw new InvalidDataException("Invalid HID output length.");
            byte[] output = new byte[length];
            await stream.ReadExactlyAsync(output, token).ConfigureAwait(false);
            return new DeviceMessage(2, null);
        }
        throw new InvalidDataException($"Unknown device message {type[0]}.");
    }
}

public sealed record DeviceMessage(byte Type, string? ClipboardText);
