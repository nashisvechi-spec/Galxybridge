using System.Buffers.Binary;
using System.Text.Json;

namespace GalaxyBridge.Core;

// Independent of ADB/scrcpy. Every frame travels inside certificate-pinned TLS.
public static class LanProtocol
{
    public const int Version = 1, Port = 38271, DiscoveryPort = 38272, MaxFrame = 65536, ChunkSize = 32768;
    public const long MaxFile = 2L * 1024 * 1024 * 1024;
    public const int MaxClipboardBytes = 16000;
    public static string ClipboardText(JsonElement message)
    {
        if (!message.TryGetProperty("text", out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Invalid clipboard text.");
        string text = value.GetString()!;
        if (text.Length == 0 || text.Length > MaxClipboardBytes || text.Contains('\0') ||
            System.Text.Encoding.UTF8.GetByteCount(text) > MaxClipboardBytes)
            throw new InvalidDataException("Invalid clipboard text size or characters.");
        return text;
    }
    public static byte[] Encode(object value)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value);
        if (body.Length is < 2 or > MaxFrame) throw new ArgumentException("Invalid LAN frame size.");
        byte[] frame = new byte[body.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length); body.CopyTo(frame, 4);
        return frame;
    }
    public static async Task<JsonDocument> ReadAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > MaxFrame) throw new InvalidDataException("Invalid LAN frame size.");
        byte[] body = new byte[length]; await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        JsonDocument result = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
        if (result.RootElement.ValueKind != JsonValueKind.Object) { result.Dispose(); throw new InvalidDataException("Invalid LAN object."); }
        return result;
    }
    public static bool HexSecret(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool Identifier(string? value) => value is { Length: 32 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool SafeName(string name) => name.Length > 0 && name is not "." and not ".." &&
        !name.Any(c => c is '/' or '\\' || char.IsControl(c)) && System.Text.Encoding.UTF8.GetByteCount(name) <= 255;
}
