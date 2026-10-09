using System.Security.Cryptography;
using System.Text.Json;

namespace GalaxyBridge.Core;

public sealed record LanReceiveState(bool Busy, long Bytes, long Size, string Message, string? SavedPath = null);

// The paired sender supplies a name, never a destination path. Only our own temporary file is removed.
public sealed class LanFileReceiver : IDisposable
{
    private readonly object gate = new();
    private FileStream? output;
    private IncrementalHash? hash;
    private string? transfer, temporary, folder, name;
    private long expected, received, lastChunk;
    private int sequence;
    private LanReceiveState state = new(false, 0, 0, "Файлы с телефона ещё не получены.");
    public LanReceiveState State { get { lock (gate) return state; } }

    public string Accept(JsonElement command, string destination, bool enabled)
    {
        lock (gate)
        {
            string type = command.GetProperty("type").GetString() ?? "";
            string incoming = command.GetProperty("transfer").GetString() ?? "";
            if (!LanProtocol.Identifier(incoming)) throw new InvalidDataException("Invalid transfer.");
            if (type == "uploadAbort") { if (incoming == transfer) CancelCore("Приём отменён телефоном."); return ""; }
            if (!enabled) { if (temporary is not null) CancelCore("Приём файлов выключен."); throw new IOException("Приём файлов на ПК выключен."); }
            if (type == "uploadBegin")
            {
                if (output is not null) throw new IOException("Другая передача ещё не завершена.");
                string supplied = command.GetProperty("name").GetString() ?? "";
                long size = command.GetProperty("size").GetInt64();
                if (!LanProtocol.SafeName(supplied) || size < -1 || size > LanProtocol.MaxFile) throw new InvalidDataException("Invalid file.");
                folder = Path.GetFullPath(destination); Directory.CreateDirectory(folder);
                name = WindowsName(supplied);
                string pending = Path.Combine(folder, ".galaxybridge-" + Guid.NewGuid().ToString("N") + ".part");
                output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                temporary = pending; transfer = incoming; expected = size; received = 0; sequence = 0;
                hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); lastChunk = Environment.TickCount64;
                state = new(true, 0, size, "Приём: " + name); return "";
            }
            if (incoming != transfer || output is null) throw new IOException("Передача уже отменена или не начата.");
            try
            {
                lastChunk = Environment.TickCount64;
                if (type == "uploadChunk")
                {
                    string encoded = command.GetProperty("data").GetString() ?? "";
                    if (encoded.Length > 43692 || command.GetProperty("seq").GetInt32() != sequence) throw new InvalidDataException("Invalid chunk.");
                    byte[] data = Convert.FromBase64String(encoded);
                    if (data.Length is < 1 or > LanProtocol.ChunkSize || received + data.Length > LanProtocol.MaxFile ||
                        (expected >= 0 && received + data.Length > expected)) throw new InvalidDataException("Invalid chunk size.");
                    output.Write(data); hash!.AppendData(data); received += data.Length; sequence++;
                    state = new(true, received, expected, "Приём: " + name); return "";
                }
                if (type != "uploadEnd") throw new InvalidDataException("Unknown upload command.");
                string checksum = command.GetProperty("sha256").GetString() ?? "";
                long size = command.GetProperty("size").GetInt64();
                if (size != received || (expected >= 0 && received != expected) || !LanProtocol.HexSecret(checksum) ||
                    !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(checksum), hash!.GetHashAndReset()))
                    throw new InvalidDataException("File checksum mismatch.");
                output.Flush(true); output.Dispose(); output = null;
                string saved = Publish();
                temporary = null; transfer = null; hash.Dispose(); hash = null;
                state = new(false, received, received, "Получен: " + Path.GetFileName(saved), saved);
                return Path.GetFileName(saved);
            }
            catch { CancelCore("Приём остановлен. Незавершённый файл удалён."); throw; }
        }
    }
    private string Publish()
    {
        string stem = Path.GetFileNameWithoutExtension(name!), extension = Path.GetExtension(name!);
        for (int index = 0; index < 10000; index++)
        {
            string target = Path.Combine(folder!, index == 0 ? name! : $"{stem} ({index}){extension}");
            try { File.Move(temporary!, target, overwrite: false); return target; }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { }
        }
        throw new IOException("Слишком много файлов с одинаковым именем.");
    }
    public static string WindowsName(string supplied)
    {
        if (!LanProtocol.SafeName(supplied)) throw new ArgumentException("Invalid name.");
        string safe = new(supplied.Select(c => "<>:\"|?*".Contains(c) ? '_' : c).Take(180).ToArray());
        if (char.IsHighSurrogate(safe[^1])) safe = safe[..^1];
        safe = safe.TrimEnd(' ', '.'); if (safe.Length == 0) safe = "file";
        string stem = safe.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³'))) safe = "_" + safe;
        return safe;
    }
    public void Expire(long now)
    { lock (gate) if (output is not null && now - lastChunk > 30000) CancelCore("Приём прерван: телефон не передаёт данные."); }
    public void Cancel(string message = "Приём отменён на ПК.")
    { lock (gate) if (temporary is not null) CancelCore(message); }
    private void CancelCore(string message)
    {
        try { output?.Dispose(); } catch (IOException) { }
        output = null; hash?.Dispose(); hash = null; transfer = null;
        string? pending = temporary; temporary = null;
        if (pending is not null)
        {
            try { File.Delete(pending); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { message = "Приём остановлен. Не удалось удалить временный файл: " + Path.GetFileName(pending); }
        }
        state = new(false, received, expected, message);
    }
    public void Dispose() => Cancel("Связь прервалась. Незавершённая передача не повторяется.");
}
