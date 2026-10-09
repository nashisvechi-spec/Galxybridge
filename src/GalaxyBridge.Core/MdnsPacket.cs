using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace GalaxyBridge.Core;

public sealed record MdnsRecord(string Name, ushort Type, string Target, int Port, string Address);

/// <summary>Bounded DNS wire codec for local ADB discovery (PTR/SRV/A only).</summary>
public static class MdnsPacket
{
    public static byte[] Query(string name, ushort type, ushort id)
    {
        using MemoryStream stream = new();
        byte[] header = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 1);
        stream.Write(header);
        int size = 1;
        foreach (string label in name.TrimEnd('.').Split('.'))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is < 1 or > 63 || (size += bytes.Length + 1) > 255)
                throw new FormatException("Invalid DNS name.");
            stream.WriteByte((byte)bytes.Length); stream.Write(bytes);
        }
        stream.WriteByte(0);
        byte[] question = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(question, type);
        // Legacy mDNS queries use an ephemeral source port and request an ordinary IN answer.
        BinaryPrimitives.WriteUInt16BigEndian(question.AsSpan(2), 1);
        stream.Write(question); return stream.ToArray();
    }

    public static IReadOnlyList<MdnsRecord> Parse(ReadOnlySpan<byte> data, ushort id)
    {
        if (data.Length is < 12 or > 9000) throw new FormatException("Invalid DNS packet length.");
        byte[] packet = data.ToArray();
        ushort responseId = Read16(packet, 0), flags = Read16(packet, 2);
        if ((responseId != id && responseId != 0) || (flags & 0xFA0F) != 0x8000)
            throw new FormatException("Not a matching complete DNS response.");
        int questions = Read16(packet, 4);
        int count = Read16(packet, 6) + Read16(packet, 8) + Read16(packet, 10);
        if (questions > 16 || count > 128) throw new FormatException("Too many DNS records.");
        int offset = 12;
        for (int i = 0; i < questions; i++)
        { _ = Name(packet, ref offset); Require(packet, offset, 4); offset += 4; }
        List<MdnsRecord> records = [];
        for (int i = 0; i < count; i++)
        {
            string owner = Name(packet, ref offset);
            Require(packet, offset, 10);
            ushort type = Read16(packet, offset), cls = Read16(packet, offset + 2);
            uint ttl = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(offset + 4, 4));
            int length = Read16(packet, offset + 8); offset += 10;
            Require(packet, offset, length); int end = offset + length;
            if ((cls & 0x7FFF) == 1 && ttl > 0)
            {
                if (type == 12)
                {
                    string target = Name(packet, ref offset);
                    if (offset != end) throw new FormatException("Invalid PTR length.");
                    records.Add(new(owner, type, target, 0, ""));
                }
                else if (type == 33)
                {
                    if (length < 7) throw new FormatException("Invalid SRV length.");
                    int port = Read16(packet, offset + 4); offset += 6;
                    string target = Name(packet, ref offset);
                    if (offset != end) throw new FormatException("Invalid SRV target length.");
                    if (port > 0) records.Add(new(owner, type, target, port, ""));
                }
                else if (type == 1 && length == 4)
                {
                    IPAddress address = new(packet.AsSpan(offset, 4));
                    if (!IPAddress.IsLoopback(address) && packet[offset] is > 0 and < 224)
                        records.Add(new(owner, type, "", 0, address.ToString()));
                }
            }
            offset = end;
        }
        return records;
    }

    private static string Name(byte[] packet, ref int offset)
    {
        int cursor = offset, resume = -1, size = 1, hops = 0;
        List<string> labels = [];
        while (true)
        {
            Require(packet, cursor, 1);
            int length = packet[cursor++];
            if (length == 0) break;
            if ((length & 0xC0) == 0xC0)
            {
                Require(packet, cursor, 1);
                int target = ((length & 0x3F) << 8) | packet[cursor++];
                if (target >= cursor - 2 || ++hops > 32) throw new FormatException("Invalid DNS compression pointer.");
                if (resume < 0) resume = cursor;
                cursor = target; continue;
            }
            if (length > 63 || (size += length + 1) > 255) throw new FormatException("Invalid DNS label.");
            Require(packet, cursor, length);
            labels.Add(Encoding.UTF8.GetString(packet, cursor, length)); cursor += length;
        }
        offset = resume >= 0 ? resume : cursor;
        return string.Join('.', labels);
    }
    private static ushort Read16(byte[] packet, int offset)
    { Require(packet, offset, 2); return BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset, 2)); }
    private static void Require(byte[] packet, int offset, int length)
    { if (offset < 0 || length < 0 || offset > packet.Length - length) throw new FormatException("Truncated DNS packet."); }
}
