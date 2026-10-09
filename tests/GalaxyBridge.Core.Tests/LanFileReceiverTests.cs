using GalaxyBridge.Core;
using System.Security.Cryptography;
using System.Text.Json;

static class LanFileReceiverTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("LAN receive: " + reason); checks++; }
        void Reject(Action action, string reason)
        {
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or FormatException) { rejected = true; }
            Check(rejected, reason);
        }
        string folder = Path.Combine(Path.GetTempPath(), "GalaxyBridgeReceiveTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using LanFileReceiver receiver = new();
        string id = new('a', 32), other = new('b', 32);
        string Accept(object value, bool enabled = true)
        { using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(value)); return receiver.Accept(json.RootElement, folder, enabled); }
        void Begin(string name, long size) => Accept(new { type = "uploadBegin", transfer = id, name, size });
        void Chunk(byte[] bytes, int seq = 0) => Accept(new { type = "uploadChunk", transfer = id, seq, data = Convert.ToBase64String(bytes) });
        string End(byte[] bytes) => Accept(new { type = "uploadEnd", transfer = id, size = (long)bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        bool NoPending() => Directory.GetFiles(folder, ".galaxybridge-*.part").Length == 0;
        try
        {
            byte[] data = Enumerable.Range(0, 70000).Select(i => (byte)i).ToArray();
            Begin("Фото 🌍.bin", data.Length);
            Check(receiver.State.Busy && !File.Exists(Path.Combine(folder, "Фото 🌍.bin")), "unfinished file never published");
            for (int index = 0; index < data.Length; index += LanProtocol.ChunkSize)
                Chunk(data[index..Math.Min(index + LanProtocol.ChunkSize, data.Length)], index / LanProtocol.ChunkSize);
            Check(receiver.State.Bytes == data.Length, "all bytes received");
            string saved = End(data);
            Check(File.ReadAllBytes(Path.Combine(folder, saved)).SequenceEqual(data), "multi-chunk SHA-256 round trip");
            Check(!receiver.State.Busy && receiver.State.SavedPath == Path.Combine(folder, saved) && NoPending(), "published state and cleanup");
            Begin("Фото 🌍.bin", 0); string duplicate = End([]);
            Check(duplicate == "Фото 🌍 (1).bin" && File.ReadAllBytes(Path.Combine(folder, saved)).SequenceEqual(data), "duplicate never overwrites");
            Directory.CreateDirectory(Path.Combine(folder, "folder.txt")); Begin("folder.txt", 0);
            Check(End([]) == "folder (1).txt", "directory name collision");
            Begin("unknown.bin", -1); Chunk([1, 2, 3]); End([1, 2, 3]);
            Check(File.ReadAllBytes(Path.Combine(folder, "unknown.bin")).SequenceEqual(new byte[] { 1, 2, 3 }), "unknown provider size supported");
            foreach (string bad in new[] { "../outside.txt", "x\\y", "..", ".", "", "x\ny", new string('Я', 128) })
                Reject(() => Begin(bad, 0), "reject unsafe file name");
            Reject(() => Begin("large", LanProtocol.MaxFile + 1), "2 GiB cap");
            Reject(() => Begin("negative", -2), "invalid unknown size");
            Reject(() => Accept(new { type = "uploadBegin", transfer = id, name = "off", size = 0 }, false), "disabled receive creates no file");
            Check(!File.Exists(Path.Combine(folder, "off")) && NoPending(), "disabled receive leaves nothing");
            Begin("canceled", 3); Chunk([1]); receiver.Cancel();
            Check(!receiver.State.Busy && NoPending() && !File.Exists(Path.Combine(folder, "canceled")), "PC cancellation removes only pending file");
            Reject(() => Chunk([2]), "canceled transfer cannot resume");
            Begin("cancel-phone", 1); Accept(new { type = "uploadAbort", transfer = id });
            Check(NoPending() && !receiver.State.Busy, "phone abort cleanup");
            Begin("active", 1);
            Reject(() => Accept(new { type = "uploadBegin", transfer = other, name = "other", size = 0 }), "single receive queue");
            Accept(new { type = "uploadAbort", transfer = other });
            Check(receiver.State.Busy, "unrelated abort does not delete active upload"); Chunk([4]); End([4]);
            Begin("sequence", 1); Reject(() => Chunk([1], 1), "out-of-order chunk rejected"); Check(NoPending(), "invalid sequence cleanup");
            Begin("oversized-chunk", -1); Reject(() => Chunk(new byte[LanProtocol.ChunkSize + 1]), "chunk bound enforced"); Check(NoPending(), "oversized chunk cleanup");
            Begin("size-mismatch", 2); Chunk([1]); Reject(() => End([1]), "short file rejected"); Check(NoPending(), "short file cleanup");
            Begin("checksum", 1); Chunk([1]); Reject(() => End([2]), "bad checksum rejected"); Check(NoPending(), "bad checksum cleanup");
            Begin("too-many-bytes", 0); Reject(() => Chunk([1]), "declared size bound"); Check(NoPending(), "size bound cleanup");
            Begin("bad-base64", -1); Reject(() => Accept(new { type = "uploadChunk", transfer = id, seq = 0, data = "%%%" }), "invalid base64 rejected"); Check(NoPending(), "bad base64 cleanup");
            Begin("disconnected", -1); receiver.Dispose(); Check(NoPending(), "disconnect cleanup");
            Begin("idle", -1); receiver.Expire(Environment.TickCount64 + 31000); Check(NoPending() && !receiver.State.Busy, "idle transfer cleanup");
            Begin("disabled-active", -1);
            Reject(() => Accept(new { type = "uploadChunk", transfer = id, seq = 0, data = "AQ==" }, false), "disable active receive");
            Check(NoPending() && !receiver.State.Busy, "disabling receiver removes pending file");
            Check(File.Exists(Path.Combine(folder, saved)), "completed files survive all aborts");
            foreach (var (source, expected) in new[] { ("CON.txt", "_CON.txt"), ("com1", "_com1"), ("LPT³.txt", "_LPT³.txt"),
                ("a:b?.txt", "a_b_.txt"), ("name. ", "name"), ("normal.jpg", "normal.jpg") })
                Check(LanFileReceiver.WindowsName(source) == expected, "Windows name normalization");
            Check(LanFileReceiver.WindowsName(new string('x', 179) + "🌍") == new string('x', 179), "truncation keeps valid Unicode");
            return checks;
        }
        finally { receiver.Dispose(); Directory.Delete(folder, true); }
    }
}
