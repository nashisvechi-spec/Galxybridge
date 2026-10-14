using System.Text.Json;

namespace GalaxyBridge.Core;

public sealed record TransferEntry(string Id, DateTimeOffset Time, string Direction, string Name,
    string Status, string? LocalPath = null, string? Source = null, string? Error = null);

public sealed class TransferJournal
{
    private readonly object gate = new();
    private readonly string path;
    private List<TransferEntry> entries = [];
    public TransferJournal(string path)
    {
        this.path = path;
        try {
            if (File.Exists(path) && new FileInfo(path).Length <= 2_000_000)
                entries = (JsonSerializer.Deserialize<List<TransferEntry>>(File.ReadAllText(path)) ?? [])
                    .Where(e => e is not null && e.Name is { Length: <= 1024 } && e.Id is { Length: <= 64 })
                    .OrderByDescending(e => e.Time).Take(100).ToList();
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public TransferEntry[] Entries { get { lock (gate) return entries.ToArray(); } }
    public void Add(TransferEntry entry)
    {
        lock (gate) {
            entries.RemoveAll(e => e.Id == entry.Id); entries.Insert(0, entry);
            if (entries.Count > 100) entries.RemoveRange(100, entries.Count - 100);
            Save();
        }
    }
    public void Clear() { lock (gate) { entries.Clear(); Save(); } }
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string pending = path + ".tmp";
        File.WriteAllText(pending, JsonSerializer.Serialize(entries));
        File.Move(pending, path, true);
    }
}
