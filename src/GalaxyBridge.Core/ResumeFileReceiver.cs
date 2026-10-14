using System.Security.Cryptography;
using System.Text.Json;

namespace GalaxyBridge.Core;

public sealed class ResumeFileBusyException(string message,Exception? inner=null) : IOException(message,inner);

// A private, bounded spool. Only verified complete files are published to the user's folder.
public sealed class ResumeFileReceiver : IDisposable
{
    private sealed record Metadata(string Name, long Size, string Hash, string? Saved, DateTimeOffset Updated);
    private readonly string cache;
    private FileStream? output;
    private Metadata? metadata;
    private string? id, destination;
    private int sequence;
    private long lastChunk;
    private int cancelRequested;
    public LanReceiveState State { get; private set; } = new(false, 0, 0, "Ожидаем файлы.");
    public long Offset => State.Bytes;
    public bool AlreadySaved { get; private set; }
    public ResumeFileReceiver(string cache) { this.cache = cache; }
    private string Part => Path.Combine(cache, id + ".part");
    private string Manifest => Path.Combine(cache, id + ".json");
    public string Accept(JsonElement message, string folder, bool enabled)
    {
        string type = message.GetProperty("type").GetString() ?? "";
        string transfer = message.GetProperty("transfer").GetString() ?? "";
        if (!LanProtocol.Identifier(transfer)) throw new InvalidDataException("Invalid transfer.");
        if (type == "uploadAbort") { if (transfer == id) Cancel(); return ""; }
        if (!enabled) { Cancel(); throw new IOException("Приём выключен."); }
        if (type == "uploadBegin") return Begin(message, transfer, folder);
        if (transfer != id || metadata is null || output is null) throw new IOException("Сначала начните докачку.");
        lastChunk = Environment.TickCount64;
        try {
            if(Volatile.Read(ref cancelRequested)!=0) throw new OperationCanceledException("Приём отменён.");
            if (type == "uploadChunk") {
                string encoded = message.GetProperty("data").GetString() ?? "";
                if (encoded.Length > 43692 || message.GetProperty("seq").GetInt32() != sequence) throw new InvalidDataException("Invalid chunk.");
                byte[] data = Convert.FromBase64String(encoded);
                if (data.Length is < 1 or > LanProtocol.ChunkSize || output.Length + data.Length > metadata.Size) throw new InvalidDataException("Invalid size.");
                output.Write(data); sequence++;
                State = State with { Bytes = output.Position }; return "";
            }
            if (type != "uploadEnd" || message.GetProperty("sha256").GetString() != metadata.Hash || output.Length != metadata.Size)
                throw new InvalidDataException("Invalid completed file.");
            output.Flush(true); output.Position = 0;
            using IncrementalHash digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer=new byte[LanProtocol.ChunkSize];int length;
            while((length=output.Read(buffer))>0) { if(Volatile.Read(ref cancelRequested)!=0) throw new OperationCanceledException();digest.AppendData(buffer,0,length); }
            string actual = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(metadata.Hash)))
                throw new InvalidDataException("File checksum mismatch.");
            output.Position = 0;
            string saved = Publish();
            output.Dispose(); output = null;
            // A lost final ACK must not create a second copy or receipt on reconnect.
            metadata = metadata with { Saved = saved, Updated = DateTimeOffset.UtcNow }; SaveMetadata();
            File.Delete(Part);
            State = new(false, metadata.Size, metadata.Size, "Получен: " + Path.GetFileName(saved), saved, id);
            return Path.GetFileName(saved);
        } catch { Cancel(); throw; }
    }
    private string Begin(JsonElement message, string transfer, string folder)
    {
        if (output is not null) { if(id==transfer) Suspend(); else throw new IOException("Другая передача ещё не завершена."); }
        string name = message.GetProperty("name").GetString() ?? "", hash = message.GetProperty("sha256").GetString() ?? "";
        long size = message.GetProperty("size").GetInt64();
        if (!LanProtocol.SafeName(name) || !LanProtocol.HexSecret(hash) || size < 0 || size > LanProtocol.MaxFile) throw new InvalidDataException("Invalid file.");
        Directory.CreateDirectory(cache); Prune();
        id = transfer; destination = Path.GetFullPath(folder); Directory.CreateDirectory(destination);
        metadata = null; AlreadySaved = false;Interlocked.Exchange(ref cancelRequested,0);
        try { if (File.Exists(Manifest) && new FileInfo(Manifest).Length < 16384) metadata = JsonSerializer.Deserialize<Metadata>(File.ReadAllText(Manifest)); }
        catch (Exception e) when (e is IOException or JsonException) { }
        if (metadata is not null && (metadata.Name != name || metadata.Size != size || metadata.Hash != hash)) {
            id = null; metadata = null; throw new InvalidDataException("Идентификатор принадлежит другому файлу.");
        }
        metadata ??= new(name, size, hash, null, DateTimeOffset.UtcNow);
        if (metadata.Saved is not null) {
            AlreadySaved = true; State = new(false, size, size, "Уже получен: " + name, metadata.Saved, id);
            return Path.GetFileName(metadata.Saved);
        }
        try { output = new FileStream(Part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch(IOException e) when((e.HResult&0xffff) is 32 or 33 or 11) { throw new ResumeFileBusyException("Завершается предыдущая попытка приёма.",e); }
        if (output.Length > size) { Cancel(); throw new InvalidDataException("Invalid partial file."); }
        output.Position = output.Length; sequence = 0; lastChunk = Environment.TickCount64;Interlocked.Exchange(ref cancelRequested,0);
        metadata = metadata with { Updated = DateTimeOffset.UtcNow }; SaveMetadata();
        State = new(true, output.Length, size, (output.Length > 0 ? "Докачка: " : "Приём: ") + name, OperationId: id);
        return "";
    }
    private string Publish()
    {
        string safe = LanFileReceiver.WindowsName(metadata!.Name), pending = Path.Combine(destination!, ".galaxybridge-" + Guid.NewGuid().ToString("N") + ".part");
        try {
            using (FileStream target = new(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                byte[] buffer=new byte[LanProtocol.ChunkSize];int length;
                while((length=output!.Read(buffer))>0) { if(Volatile.Read(ref cancelRequested)!=0) throw new OperationCanceledException();target.Write(buffer,0,length); }
                target.Flush(true);
            }
            string stem = Path.GetFileNameWithoutExtension(safe), extension = Path.GetExtension(safe);
            for (int i = 0; i < 10000; i++) {
                string path = Path.Combine(destination!, i == 0 ? safe : $"{stem} ({i}){extension}");
                try { File.Move(pending, path, false); return path; }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { }
            }
            throw new IOException("Слишком много одинаковых имён.");
        } finally { if (File.Exists(pending)) File.Delete(pending); }
    }
    private void SaveMetadata() { string temp=Manifest+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(metadata));File.Move(temp,Manifest,true); }
    private void Prune()
    {
        FileInfo[] files = new DirectoryInfo(cache).GetFiles("*.json").Where(f => LanProtocol.Identifier(Path.GetFileNameWithoutExtension(f.Name))).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        for (int i = 0; i < files.Length; i++) if (i >= 10 || DateTime.UtcNow - files[i].LastWriteTimeUtc > TimeSpan.FromHours(24)) {
            string part = Path.ChangeExtension(files[i].FullName, ".part"); File.Delete(part); files[i].Delete();
        }
    }
    public void Suspend()
    {
        try { output?.Dispose(); } catch(IOException) { } finally { output=null; }
        if (State.Busy) State = State with { Busy = false, Message = "Связь прервалась. Файл сохранён для докачки." };
    }
    public void RequestCancel() => Interlocked.Exchange(ref cancelRequested,1);
    public void Expire(long now) { if(Volatile.Read(ref cancelRequested)!=0) Cancel();else if (State.Busy && now - lastChunk > 30000) Suspend(); }
    public void Cancel()
    {
        Suspend();string message="Приём отменён.";
        if(id is not null) try { File.Delete(Part);File.Delete(Manifest); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { message="Приём отменён. Временный файл будет очищен позже."; }
        id=null;metadata=null;State=State with {Busy=false,Message=message};
    }
    public void Dispose() => Suspend();
}
