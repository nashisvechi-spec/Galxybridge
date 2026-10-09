using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GalaxyBridge.Core;
namespace GalaxyBridge.Windows;

internal sealed class LanSession : IPhoneControl
{
    private readonly TcpClient socket;
    private readonly SslStream stream;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<byte[]> outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(512)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> replies = new();
    private readonly HashSet<byte> held = [];
    private PhoneEdgeSample? edge;
    private int epoch, closed;
    private volatile bool inputReady;
    public string Name { get; }
    public bool InputReady => inputReady && IsAlive;
    public bool IsAlive => Volatile.Read(ref closed) == 0;
    public PhoneEdgeSample? LatestEdge => Volatile.Read(ref edge);
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event Action? Lost;
    public LanSession(TcpClient socket, SslStream stream, string name) { this.socket = socket; this.stream = stream; Name = name; }
    public void Start() => Completion = RunAsync();
    public bool Post(object value)
    {
        if (!IsAlive) return false;
        if (outgoing.Writer.TryWrite(LanProtocol.Encode(value))) return true;
        Close(); return false;
    }
    public int BeginEdgeReturn(PhoneSide side)
    {
        held.Clear(); edge = null; int id = checked(++epoch);
        Post(new { type = "capture", active = true, epoch = id, side = (int)side }); return id;
    }
    public void EndEdgeReturn() { edge = null; Post(new { type = "capture", active = false, epoch, side = 0 }); }
    public void ReleaseInputs() { held.Clear(); Post(new { type = "release" }); }
    public void Mouse(byte buttons, int dx, int dy, int wheel = 0) => Post(new { type = "mouse", buttons, dx, dy, wheel });
    public void Keyboard(byte[] report)
    {
        if (report.Length != 8) throw new ArgumentException("Keyboard report.");
        HashSet<byte> keys = report.Skip(2).Where(k => k > 1).ToHashSet();
        foreach (byte key in keys.Except(held))
        {
            bool ctrl = (report[0] & 0x11) != 0, alt = (report[0] & 0x44) != 0, win = (report[0] & 0x88) != 0;
            if (ctrl)
            {
                string? action = key switch { 4 => "selectAll", 6 => "copy", 27 => "cut", _ => null };
                if (action is not null) Post(new { type = "edit", action });
                continue;
            }
            if (alt || win) continue;
            int code = key switch { 0x28 => 66, 0x2A => 67, 0x2B => 61, 0x29 => 4, 0x4C => 112,
                0x4F => 22, 0x50 => 21, 0x51 => 20, 0x52 => 19, _ => 0 };
            if (code > 0) Post(new { type = "key", code });
            else if (LanTextInput.Translate(key, report[0]) is { Length: > 0 } text) Post(new { type = "text", text });
        }
        held.Clear(); held.UnionWith(keys);
    }
    public bool Send(byte[] packet)
    {
        // InputCapture's paste shortcut supplies the existing scrcpy-shaped clipboard packet.
        if (packet.Length < 14 || packet[0] != 9 || packet.Length - 14 != BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(10))) return false;
        string text = Encoding.UTF8.GetString(packet, 14, packet.Length - 14);
        if (Encoding.UTF8.GetByteCount(text) > 16000) throw new ArgumentException("Вставка в Wi-Fi режиме ограничена 16 КБ текста.");
        return Post(new { type = "text", text });
    }
    private async Task RunAsync()
    {
        Task writer = WriteAsync(), ping = PingAsync();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using CancellationTokenSource read = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                read.CancelAfter(TimeSpan.FromSeconds(12));
                using JsonDocument message = await LanProtocol.ReadAsync(stream, read.Token);
                JsonElement root = message.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "pong": inputReady = root.GetProperty("ready").GetBoolean(); break;
                    case "edge":
                        if (PhoneEdgeSample.TryParse(root.GetProperty("line").GetString() ?? "", Environment.TickCount64, out PhoneEdgeSample? sample))
                            Volatile.Write(ref edge, sample);
                        break;
                    case "ack":
                        string id = root.GetProperty("id").GetString() ?? "";
                        if (replies.TryRemove(id, out var completion))
                        {
                            if (root.GetProperty("ok").GetBoolean()) completion.TrySetResult(root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "");
                            else completion.TrySetException(new IOException("Телефон не сохранил файл. Проверьте свободное место и повторите отправку."));
                        }
                        break;
                    default: throw new InvalidDataException("Unknown LAN response.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or
            JsonException or KeyNotFoundException or InvalidOperationException) { }
        finally
        {
            Close(); await Task.WhenAll(writer, ping);
            foreach (var reply in replies.Values) reply.TrySetException(new IOException("Связь прервалась. Отправка не повторяется автоматически."));
            replies.Clear(); lifetime.Dispose(); Lost?.Invoke();
        }
    }
    private async Task WriteAsync()
    {
        try { await foreach (byte[] packet in outgoing.Reader.ReadAllAsync(lifetime.Token)) await stream.WriteAsync(packet, lifetime.Token); }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { Close(); }
    }
    private async Task PingAsync()
    {
        try { while (!lifetime.IsCancellationRequested) { Post(new { type = "ping" }); await Task.Delay(3000, lifetime.Token); } }
        catch (OperationCanceledException) { }
    }
    private async Task<string> RequestAsync(Func<string, object> build, CancellationToken ct)
    {
        string id = Guid.NewGuid().ToString("N");
        TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously); replies[id] = result;
        try
        {
            if (!Post(build(id))) throw new IOException("Телефон отключён.");
            return await result.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
        finally { replies.TryRemove(id, out _); }
    }
    public async Task<string> SendFileAsync(string path, CancellationToken ct)
    {
        string name = Path.GetFileName(path);
        if (!LanProtocol.SafeName(name)) throw new ArgumentException("Недопустимое имя файла.");
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, LanProtocol.ChunkSize, true);
        long size = file.Length;
        if (size > LanProtocol.MaxFile) throw new ArgumentException("В тестовом режиме размер файла ограничен 2 ГБ.");
        string transfer = Guid.NewGuid().ToString("N");
        bool finished = false;
        try
        {
            await RequestAsync(id => new { type = "fileBegin", id, transfer, name, size }, ct);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[LanProtocol.ChunkSize]; int seq = 0; long sent = 0;
            int length;
            while ((length = await file.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer, 0, length); sent += length;
                if (sent > size) throw new IOException("Файл изменился во время отправки.");
                string data = Convert.ToBase64String(buffer, 0, length);
                await RequestAsync(id => new { type = "fileChunk", id, transfer, seq, data }, ct); seq++;
            }
            if (sent != size) throw new IOException("Файл изменился во время отправки.");
            string sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            string saved = await RequestAsync(id => new { type = "fileEnd", id, transfer, sha256 }, ct);
            finished = true; return saved;
        }
        finally
        {
            if (!finished)
            {
                // Abort is ordered after any already-enqueued chunk. Never reconnect/replay a partial transfer.
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(2));
                try { await RequestAsync(id => new { type = "fileAbort", id, transfer }, cleanup.Token); }
                catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) { }
            }
        }
    }
    public void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        inputReady = false; outgoing.Writer.TryComplete(); lifetime.Cancel(); socket.Dispose();
    }
}
