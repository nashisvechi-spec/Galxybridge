using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
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
    private sealed record ReceiveConfiguration(bool Enabled, string Folder);
    private volatile ReceiveConfiguration receiving = new(false, "");
    private readonly object receiveGate = new();
    private readonly LanFileReceiver incoming = new();
    public LanReceiveState ReceiveState => incoming.State;
    public void ConfigureReceive(bool enabled, string folder)
    {
        lock (receiveGate)
        {
            if (!enabled || receiving.Folder != folder) incoming.Cancel();
            receiving = new(enabled, folder);
        }
    }
    public void CancelReceive() => incoming.Cancel();
    private PhoneEdgeSample? edge;
    private int epoch, closed;
    private volatile bool inputReady;
    private volatile bool clipboardEnabled;
    public bool AutomaticClipboard { get; }
    public Func<uint>? ClipboardSequenceReader { get; set; }
    public Func<bool>? ClipboardReadAllowed { get; set; }
    public Func<string, uint, CancellationToken, Task<bool>>? AutomaticClipboardWriter { get; set; }
    public event Action<string>? ClipboardStatus;
    private readonly object clipboardGate = new();
    private sealed record ClipboardPull(string Id, uint Sequence, CancellationTokenSource Cancel, CancellationToken Token);
    private ClipboardPull? clipboardPull;
    private string? clipboardSetId;
    private bool controlling;
    private volatile Func<string, CancellationToken, Task<bool>>? clipboardWriter;
    public Func<string, CancellationToken, Task<bool>>? ClipboardWriter { get => clipboardWriter; set => clipboardWriter = value; }
    public void ConfigureClipboard(bool enabled)
    {
        clipboardEnabled = enabled;
        if (!enabled) CancelClipboardPull();
        if (AutomaticClipboard) Post(new { type = "clipboardConfig", enabled });
    }
    public void CancelClipboardPull()
    {
        lock (clipboardGate)
        {
            clipboardPull?.Cancel.Cancel(); clipboardPull?.Cancel.Dispose(); clipboardPull = null;
        }
    }
    public bool PushClipboard(string text)
    {
        if (!clipboardEnabled || !AutomaticClipboard || !IsAlive) return false;
        string id = Guid.NewGuid().ToString("N");
        // Validate text and the encoded frame before it reaches the input queue.
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new { text }));
        LanProtocol.ClipboardText(document.RootElement);
        var message = new { type = "clipboardSet", id, text };
        LanProtocol.Encode(message);
        lock (clipboardGate) clipboardSetId = id;
        return Post(message);
    }
    public string Name { get; }
    public bool InputReady => inputReady && IsAlive;
    public bool IsAlive => Volatile.Read(ref closed) == 0;
    public PhoneEdgeSample? LatestEdge => Volatile.Read(ref edge);
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event Action? Lost;
    public LanSession(TcpClient socket, SslStream stream, string name, bool automaticClipboard = false)
    { this.socket = socket; this.stream = stream; Name = name; AutomaticClipboard = automaticClipboard; }
    public void Start() => Completion = RunAsync();
    public bool Post(object value)
    {
        if (!IsAlive) return false;
        if (outgoing.Writer.TryWrite(LanProtocol.Encode(value))) return true;
        Close(); return false;
    }
    public int BeginEdgeReturn(PhoneSide side)
    {
        CancelClipboardPull(); controlling = true;
        held.Clear(); edge = null; int id = checked(++epoch);
        Post(new { type = "capture", active = true, epoch = id, side = (int)side }); return id;
    }
    public void EndEdgeReturn()
    {
        edge = null; Post(new { type = "capture", active = false, epoch, side = 0 });
        bool wasControlling = controlling; controlling = false;
        if (!wasControlling || !AutomaticClipboard || !clipboardEnabled || !IsAlive || ClipboardSequenceReader is null || ClipboardReadAllowed?.Invoke() == false) return;
        CancelClipboardPull();
        var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ClipboardPull request = new(Guid.NewGuid().ToString("N"), ClipboardSequenceReader(), cancel, cancel.Token);
        lock (clipboardGate)
        {
            if (!IsAlive) { cancel.Dispose(); return; }
            clipboardPull = request;
        }
        Post(new { type = "clipboardGet", request = request.Id });
    }
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
                    case "clipboardSetAck":
                        string setId = root.GetProperty("id").GetString() ?? "";
                        if (!AutomaticClipboard || !LanProtocol.Identifier(setId)) throw new InvalidDataException("Invalid clipboard reply.");
                        bool latest;
                        lock (clipboardGate) { latest = setId == clipboardSetId; if (latest) clipboardSetId = null; }
                        if (latest && clipboardEnabled) ClipboardStatus?.Invoke(root.GetProperty("ok").GetBoolean()
                            ? "Текст с ПК записан в буфер телефона. Можно вставлять." : "Телефон не принял текст в буфер. Разблокируйте экран и повторите копирование.");
                        break;
                    case "clipboardResult":
                        string pullId = root.GetProperty("request").GetString() ?? "";
                        if (!AutomaticClipboard || !LanProtocol.Identifier(pullId)) throw new InvalidDataException("Invalid clipboard result.");
                        bool currentPull;
                        lock (clipboardGate)
                        {
                            currentPull = clipboardPull?.Id == pullId && !clipboardPull.Token.IsCancellationRequested;
                            if (currentPull) CancelClipboardPull();
                        }
                        if (currentPull)
                        {
                            string status = root.GetProperty("status").GetString() ?? "";
                            ClipboardStatus?.Invoke(status switch {
                                "permission" => "На телефоне разрешите Galaxy Bridge «Поверх других приложений» для общего буфера.",
                                "unavailable" => "Буфер телефона недоступен. Разблокируйте экран и повторите переход на ПК.",
                                "unsupported" => "Буфер телефона не содержит обычного текста до 16 КБ.",
                                _ => "Общий текстовый буфер готов. Копируйте и вставляйте." });
                        }
                        break;
                    case "clipboard":
                        string clipboardId = root.GetProperty("id").GetString() ?? "";
                        if (!LanProtocol.Identifier(clipboardId)) throw new InvalidDataException("Invalid clipboard request.");
                        string clipboardText = LanProtocol.ClipboardText(root);
                        bool copied = false;
                        string clipboardError = "Буфер ПК занят или приём текста выключен. Повторите отправку.";
                        var clipboardWriter = ClipboardWriter;
                        ClipboardPull? pull = null;
                        bool automatic = root.TryGetProperty("request", out JsonElement requestValue);
                        if (automatic)
                        {
                            string requestId = requestValue.GetString() ?? "";
                            if (!AutomaticClipboard || !LanProtocol.Identifier(requestId)) throw new InvalidDataException("Invalid automatic clipboard request.");
                            lock (clipboardGate) pull = clipboardPull?.Id == requestId ? clipboardPull : null;
                        }
                        if (!clipboardEnabled) clipboardError = "Включите «Общий текстовый буфер» в окне Wi-Fi на ПК.";
                        else if (automatic)
                        {
                            if (pull is not null && !pull.Token.IsCancellationRequested && ClipboardSequenceReader?.Invoke() == pull.Sequence && AutomaticClipboardWriter is { } autoWriter)
                            {
                                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, pull.Token);
                                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                                try { copied = await autoWriter(clipboardText, pull.Sequence, deadline.Token).WaitAsync(deadline.Token); }
                                catch (Exception ex) when (ex is OperationCanceledException or ExternalException or InvalidOperationException) { }
                            }
                            lock (clipboardGate) { if (clipboardPull == pull) CancelClipboardPull(); }
                        }
                        else if (clipboardWriter is not null)
                        {
                            using CancellationTokenSource clipboardDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                            clipboardDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                            try { copied = await clipboardWriter(clipboardText, clipboardDeadline.Token).WaitAsync(clipboardDeadline.Token); }
                            catch (Exception ex) when (ex is OperationCanceledException or ExternalException or InvalidOperationException) { }
                        }
                        Post(new { type = "clipboardAck", id = clipboardId, ok = copied, error = copied ? "" : clipboardError });
                        break;
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
                    case "uploadBegin": case "uploadChunk": case "uploadEnd": case "uploadAbort":
                        string request = root.GetProperty("id").GetString() ?? "";
                        if (!LanProtocol.Identifier(request)) throw new InvalidDataException("Invalid upload request.");
                        try
                        {
                            string saved;
                            lock (receiveGate)
                            {
                                ReceiveConfiguration options = receiving;
                                saved = incoming.Accept(root, options.Folder, options.Enabled);
                            }
                            Post(new { type = "uploadAck", id = request, ok = true, name = saved });
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
                            FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
                        {
                            Post(new { type = "uploadAck", id = request, ok = false,
                                error = receiving.Enabled ? "ПК не принял файл. Проверьте папку, место на диске или отмену приёма." : "Приём файлов на ПК выключен." });
                        }
                        break;
                    default: throw new InvalidDataException("Unknown LAN response.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException or OperationCanceledException or ObjectDisposedException or
            JsonException or KeyNotFoundException or InvalidOperationException) { }
        finally
        {
            Close(); await Task.WhenAll(writer, ping); incoming.Dispose();
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
        try { while (!lifetime.IsCancellationRequested) { incoming.Expire(Environment.TickCount64); Post(new { type = "ping" }); await Task.Delay(3000, lifetime.Token); } }
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
        CancelClipboardPull();
        incoming.Cancel("Связь прервалась. Незавершённая передача не повторяется.");
        inputReady = false; outgoing.Writer.TryComplete(); lifetime.Cancel(); socket.Dispose();
    }
}
