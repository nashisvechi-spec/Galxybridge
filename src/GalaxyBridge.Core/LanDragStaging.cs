using System.Text.Json;

namespace GalaxyBridge.Core;

// Sender supplies only an opaque batch ID. Destination paths are always host-owned.
public sealed class LanDragStaging : IDisposable
{
    private readonly LanFileReceiver receiver = new();
    private readonly List<string> files = [];
    private string? batch, folder;
    private int expected;
    private long bytes;
    private bool ready;
    public LanReceiveState State => receiver.State;
    public void Begin(string id, int count)
    {
        if (!LanProtocol.Identifier(id) || count is < 1 or > 100) throw new InvalidDataException("Invalid drag batch.");
        Dispose(); batch = id; expected = count;
        folder = Path.Combine(Path.GetTempPath(), "GalaxyBridge-Drag", Guid.NewGuid().ToString("N"));
    }
    public string Accept(JsonElement command)
    {
        Match(command.GetProperty("dragBatch").GetString() ?? "");
        if (ready) throw new IOException("Drag files are already prepared.");
        string type = command.GetProperty("type").GetString() ?? "";
        if (type == "uploadBegin" && (files.Count >= expected ||
            command.GetProperty("size").GetInt64() > LanProtocol.MaxFile - bytes))
            throw new IOException("Размер всей подборки ограничен 2 ГБ.");
        if (type == "uploadChunk")
        {
            // Base64 is bounded before decoding, as in the ordinary receiver.
            string encoded = command.GetProperty("data").GetString() ?? "";
            if (encoded.Length > 43692 || bytes + receiver.State.Bytes + Convert.FromBase64String(encoded).Length > LanProtocol.MaxFile)
            { Dispose(); throw new IOException("Размер всей подборки ограничен 2 ГБ."); }
        }
        string saved = receiver.Accept(command, folder!, true);
        if (type == "uploadEnd") { files.Add(receiver.State.SavedPath!); bytes += receiver.State.Bytes; }
        if (type == "uploadAbort") Dispose();
        return saved;
    }
    public void Ready(string id)
    {
        Match(id);
        if (receiver.State.Busy || files.Count != expected) throw new IOException("Файлы ещё не подготовлены.");
        ready = true;
    }
    public string[] Take(string id)
    {
        Match(id);
        if (!ready || !files.All(File.Exists)) throw new IOException("Подготовьте файлы заново.");
        string[] result = files.ToArray();
        // Ownership transfers to the Windows drag source, which may outlive the TLS session.
        batch = folder = null; files.Clear(); ready = false; bytes = 0;
        return result;
    }
    public bool IsReady(string id) => batch == id && ready;
    public void Cancel(string id) { Match(id); Dispose(); }
    private void Match(string id)
    { if (batch is null || batch != id) throw new IOException("Подготовьте файлы заново."); }
    public void Expire(long now) => receiver.Expire(now);
    public void Dispose()
    {
        receiver.Cancel();
        if (folder is not null) DeleteOwnedFolder(folder);
        batch = folder = null; files.Clear(); ready = false; bytes = 0;
    }
    public static void DeleteOwnedFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public static void PruneCache()
    {
        string root = Path.Combine(Path.GetTempPath(), "GalaxyBridge-Drag");
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (string path in Directory.GetDirectories(root))
                if (LanProtocol.Identifier(Path.GetFileName(path)) &&
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 &&
                    Directory.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) DeleteOwnedFolder(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

public static class FileDragHandoff
{
    public static bool CanReturn(PhoneEdgeSample sample, int epoch, PhoneSide side, byte hostButtons,
        bool keyHeld, int dx, int dy, long now)
    {
        if (epoch < 1 || sample.CaptureId != epoch || sample.Buttons != 1 ||
            hostButtons != 1 || keyHeld || now < sample.ReceivedAt || now - sample.ReceivedAt > 500) return false;
        if (sample.Zone is DesktopBounds zone)
        {
            if (zone.X < 0 || zone.Y < 0 || zone.Width < 1 || zone.Height < 1 ||
                zone.X > sample.Width-zone.Width || zone.Y > sample.Height-zone.Height ||
                sample.X < zone.X || sample.Y < zone.Y || sample.X >= zone.X+zone.Width || sample.Y >= zone.Y+zone.Height) return false;
            return side switch
            {
                PhoneSide.Right => zone.Width <= 4 && zone.Height > 4 && zone.X < sample.Width / 4 && dx < 0,
                PhoneSide.Left => zone.Width <= 4 && zone.Height > 4 && zone.X >= sample.Width * 3 / 4 && dx > 0,
                PhoneSide.Top => zone.Height <= 4 && zone.Width > 4 && zone.Y >= sample.Height * 3 / 4 && dy > 0,
                PhoneSide.Bottom => zone.Height <= 4 && zone.Width > 4 && zone.Y < sample.Height / 4 && dy < 0,
                _ => false
            };
        }
        return side switch
        {
            PhoneSide.Right => sample.X < 16 && dx < 0,
            PhoneSide.Left => sample.X >= sample.Width - 16 && dx > 0,
            PhoneSide.Top => sample.Y >= sample.Height - 16 && dy > 0,
            PhoneSide.Bottom => sample.Y < 16 && dy < 0,
            _ => false
        };
    }
}
