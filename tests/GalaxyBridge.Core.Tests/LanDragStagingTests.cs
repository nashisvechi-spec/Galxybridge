using GalaxyBridge.Core;
using System.Security.Cryptography;
using System.Text.Json;

static class LanDragStagingTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks++; }
        void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { rejected = true; }
            Check(rejected, name);
        }
        using LanDragStaging stage = new();
        string batch = new('a', 32), other = new('b', 32), transfer = new('c', 32);
        string Accept(object value)
        { using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value)); return stage.Accept(doc.RootElement); }
        void File(string name, byte[] data)
        {
            Accept(new { type = "uploadBegin", dragBatch = batch, transfer, name, size = data.Length });
            if (data.Length > 0) Accept(new { type = "uploadChunk", dragBatch = batch, transfer, seq = 0, data = Convert.ToBase64String(data) });
            Accept(new { type = "uploadEnd", dragBatch = batch, transfer, size = data.Length, sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() });
        }
        Reject(() => stage.Begin("../outside", 1), "batch cannot supply a path");
        Reject(() => stage.Begin(batch, 0), "empty drag rejected");
        Reject(() => stage.Begin(batch, 101), "drag count bounded");
        stage.Begin(batch, 2);
        Reject(() => stage.Ready(batch), "cannot drag incomplete preparation");
        Reject(() => stage.Take(batch), "cannot take unprepared files");
        File("Фото 🌍.txt", [1, 2, 3]);
        string first = stage.State.SavedPath!;
        Check(FileInfoLength(first) == 3, "prepared bytes in private cache");
        Reject(() => stage.Ready(batch), "partial batch cannot drag");
        Reject(() => stage.Cancel(other), "stale cancellation cannot delete current batch");
        Check(System.IO.File.Exists(first), "stale cancel preserved staged file");
        File("Фото 🌍.txt", [4]);
        string second = stage.State.SavedPath!;
        Check(first != second && System.IO.File.ReadAllBytes(first).SequenceEqual(new byte[] { 1, 2, 3 }), "duplicate names preserve both originals");
        Reject(() => File("extra", []), "file count enforced");
        stage.Ready(batch); Check(stage.IsReady(batch) && !stage.IsReady(other), "only current batch ready");
        Reject(() => stage.Take(other), "stale batch cannot take current files");
        string[] files = stage.Take(batch);
        Check(files.Length == 2 && files.All(System.IO.File.Exists), "prepared files leased to Windows");
        Reject(() => stage.Take(batch), "drag cannot be replayed");
        stage.Dispose(); Check(files.All(System.IO.File.Exists), "disconnect cannot delete shell-owned files");
        LanDragStaging.DeleteOwnedFolder(Path.GetDirectoryName(files[0])!);
        Check(!System.IO.File.Exists(first), "cancelled shell drag cache cleaned");
        stage.Begin(batch, 1); File("cancel", []); string canceled = stage.State.SavedPath!;
        stage.Cancel(batch); Check(!System.IO.File.Exists(canceled), "cancel removes prepared files");
        stage.Begin(batch, 1); File("old", []); string old = stage.State.SavedPath!;
        stage.Begin(other, 1); Check(!System.IO.File.Exists(old), "new batch replaces old cache");
        Reject(() => Accept(new { type = "uploadBegin", dragBatch = batch, transfer, name = "stale", size = 0 }), "old upload rejected after new preparation");
        batch = other;
        Reject(() => Accept(new { type = "uploadBegin", dragBatch = batch, transfer, name = "too-large", size = LanProtocol.MaxFile + 1 }), "batch total size bound");
        Accept(new { type = "uploadBegin", dragBatch = batch, transfer, name = "partial", size = -1 });
        Accept(new { type = "uploadChunk", dragBatch = batch, transfer, seq = 0, data = "AQ==" });
        stage.Cancel(batch); Check(!stage.State.Busy, "cancel releases active stream");
        stage.Begin(batch, 2); File("first-byte", [1]);
        Reject(() => Accept(new { type = "uploadBegin", dragBatch = batch, transfer, name = "remaining", size = LanProtocol.MaxFile }),
            "aggregate cap includes previously prepared bytes");
        stage.Dispose();

        foreach (PhoneSide side in Enum.GetValues<PhoneSide>())
        {
            double x = side == PhoneSide.Right ? 0 : side == PhoneSide.Left ? 999 : 500;
            double y = side == PhoneSide.Bottom ? 0 : side == PhoneSide.Top ? 1999 : 1000;
            int dx = side == PhoneSide.Right ? -1 : side == PhoneSide.Left ? 1 : 0;
            int dy = side == PhoneSide.Top ? 1 : side == PhoneSide.Bottom ? -1 : 0;
            var sample = new PhoneEdgeSample(7, 1, 1000, 2000, x, y, 1, 1000);
            bool Can(PhoneEdgeSample s, int epoch = 7, byte buttons = 1, bool key = false, long now = 1001) =>
                FileDragHandoff.CanReturn(s, epoch, side, buttons, key, dx, dy, now);
            Check(Can(sample), "file drag handoff on each edge");
            Check(!Can(sample, 6) && !Can(sample, buttons: 0) && !Can(sample, key: true), "old capture/released button/modifier rejected");
            Check(!Can(sample with { Buttons = 0 }) && !Can(sample with { Buttons = 3 }), "only a held left drag can return");
            Check(!Can(sample, now: 1501) && !Can(sample, now: 999), "delayed and future samples rejected");
            Check(!Can(sample with { X = 500, Y = 1000 }), "middle of phone cannot hand off");
            Check(!FileDragHandoff.CanReturn(sample, 7, side, 1, false, -dx, -dy, 1001), "inward direction cannot hand off");
            DesktopBounds zone = side switch
            {
                PhoneSide.Right => new(18, 24, 1, 1930),
                PhoneSide.Left => new(981, 24, 1, 1930),
                PhoneSide.Top => new(18, 1954, 964, 1),
                _ => new(18, 24, 964, 1)
            };
            var inset = sample with { X = zone.Width == 1 ? zone.X : 500, Y = zone.Height == 1 ? zone.Y : 1000, Zone = zone };
            Check(Can(inset), "file edge handoff uses measured navigation/cutout inset");
            Check(!Can(inset, epoch: 6) && !Can(inset, buttons: 0) && !Can(inset, key: true), "inset edge still requires same capture and physical hold");
            Check(!Can(inset with { X = 500, Y = 1000 }), "outside the actual inset strip rejected");
            Check(!Can(inset with { Zone = new(0, 0, 1000, 2000) }), "full screen cannot be a file edge");
            Check(!Can(inset with { X = 500, Y = 1000, Zone = zone.Width == 1 ? new(500, 0, 1, 2000) : new(0, 1000, 1000, 1) }), "a middle-of-screen thin strip cannot hand off");
            Check(!FileDragHandoff.CanReturn(inset, 7, side, 1, false, -dx, -dy, 1001), "inset drag still requires outward motion");
        }
        return checks;
    }
    private static long FileInfoLength(string path) => new FileInfo(path).Length;
}
