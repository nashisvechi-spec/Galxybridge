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
    private readonly LanDragStaging dragFiles = new();
    private bool progressStaging;
    private volatile bool fileEdgeEnabled;
    public bool FileEdgeConfigurationSupported { get; }
    public bool FileEdgeEnabled => fileEdgeEnabled;
    public void ConfigureFileEdge(bool enabled)
    {
        lock (receiveGate) { fileEdgeEnabled = enabled; if (!enabled) dragFiles.Dispose(); }
        if (FileEdgeConfigurationSupported) Post(new { type = "fileEdgeConfig", enabled });
    }
    public event Action<string, PhoneEdgeSample>? FileDragRequested;
    public LanReceiveState ReceiveState { get { lock (receiveGate) return dragFiles.State.Busy ? dragFiles.State : incoming.State; } }
    public (LanReceiveState State, bool Staging) ReceiveProgress
    { get { lock (receiveGate) return (progressStaging ? dragFiles.State : incoming.State, progressStaging); } }
    public string[] TakeDragFiles(string batch) { lock (receiveGate) { if (!receiving.Enabled) throw new IOException("Приём файлов выключен."); return dragFiles.Take(batch); } }
    public void ConfigureReceive(bool enabled, string folder)
    {
        lock (receiveGate)
        {
            if (!enabled || receiving.Folder != folder) { incoming.Cancel(); dragFiles.Dispose(); }
            receiving = new(enabled, folder);
        }
    }
    public void CancelReceive() { lock (receiveGate) { incoming.Cancel(); dragFiles.Dispose(); } }
    public void CancelReceive(string transfer, bool staging)
    {
        lock (receiveGate)
        {
            LanReceiveState active = staging ? dragFiles.State : incoming.State;
            // A delayed Cancel from an old dialog must not affect the next file.
            if (!active.Busy || active.OperationId != transfer) return;
            if (staging) dragFiles.Dispose(); else incoming.Cancel();
        }
    }
    private PhoneEdgeSample? edge;
    private volatile bool touchActive;
    private byte touchModifiers;
    public bool WheelZoomSupported { get; }
    public bool WheelZoomEnabled { get; set; }
    private readonly object nativeEdgeGate = new();
    private PhoneEdgeSample? nativeEdge;
    private int nativeEpoch;
    private long nativeSequence;
    private volatile bool nativeEdgeReady;
    public bool NativeEdgeSupported { get; }
    public PhoneEdgeSample? LatestNativeEdge { get { lock (nativeEdgeGate) return nativeEdgeReady && IsAlive && nativeEdge is { } sample &&
        sample.CaptureId == nativeEpoch ? sample : null; } }
    public event Action<string>? NativeEdgeStatus;
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
    private volatile bool preferWifiInput;
    private int controlDisplay=-1;
    private long controlRevision=-1;
    public bool PreferWifiInput => preferWifiInput;
    public event Action? ControlDisplayChanged;
    public bool IsAlive => Volatile.Read(ref closed) == 0;
    public PhoneEdgeSample? LatestEdge
    {
        get
        {
            PhoneEdgeSample? sample = Volatile.Read(ref edge);
            return touchActive && IsAlive && sample?.CaptureId == epoch ? sample : null;
        }
    }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event Action? Lost;
    public LanSession(TcpClient socket, SslStream stream, string name, bool automaticClipboard = false, bool nativeEdgeSupported = false, bool wheelZoomSupported = false, bool fileEdgeConfigurationSupported = false)
    { this.socket = socket; this.stream = stream; Name = name; AutomaticClipboard = automaticClipboard; NativeEdgeSupported = nativeEdgeSupported; WheelZoomSupported = wheelZoomSupported; FileEdgeConfigurationSupported = fileEdgeConfigurationSupported; }
    public void Start() => Completion = RunAsync();
    public bool Post(object value)
    {
        if (!IsAlive) return false;
        if (outgoing.Writer.TryWrite(LanProtocol.Encode(value))) return true;
        Close(); return false;
    }
    public int BeginEdgeReturn(PhoneSide side)
    {
        BeginNativeControl();
        held.Clear(); touchModifiers = 0; touchActive = false; edge = null; int id = checked(++epoch); touchActive = true;
        Post(new { type = "capture", active = true, epoch = id, side = (int)side }); return id;
    }
    public void EndEdgeReturn()
    {
        touchActive = false; touchModifiers = 0; edge = null; Post(new { type = "capture", active = false, epoch, side = 0 });
        EndNativeControl();
    }
    private long controlGeneration;
    public int BeginNativeEdgeReturn(PhoneSide side)
    {
        if (!NativeEdgeSupported || !IsAlive)
        {
            NativeEdgeStatus?.Invoke("Для возврата Bluetooth-мыши через край подключите Wi-Fi и обновите APK до 0.7.8.");
            return 0;
        }
        lock (nativeEdgeGate)
        {
            nativeEdgeReady = false; nativeEdge = null; nativeSequence = 0;
            int id = checked(++epoch); nativeEpoch = id;
            return Post(new { type = "nativeCapture", active = true, epoch = id, side = (int)side }) ? id : 0;
        }
    }
    public void EndNativeEdgeReturn(int id)
    {
        lock (nativeEdgeGate)
        {
            if (id == 0 || nativeEpoch != id) return;
            nativeEpoch = 0; nativeEdgeReady = false; nativeEdge = null;
            Post(new { type = "nativeCapture", active = false, epoch = id, side = 0 });
        }
    }
    public long BeginNativeControl() { CancelClipboardPull(); controlling = true; return Interlocked.Increment(ref controlGeneration); }
    public void EndNativeControl(long? expectedGeneration = null)
    {
        if (expectedGeneration is { } expected && expected != Volatile.Read(ref controlGeneration)) return;
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
    public void ReleaseInputs() { held.Clear(); touchModifiers = 0; touchActive = false; edge = null; Post(new { type = "release" }); }
    // Ordinary movement uses the 0.7.8 packet unchanged. Zoom is wheel-only;
    // native HID bypasses this path.
    public void Mouse(byte buttons, int dx, int dy, int wheel = 0, int horizontalWheel = 0)
    {
        bool zoom = wheel != 0 && buttons == 0 && ((touchModifiers & 0x11) != 0 || WheelZoomEnabled);
        if (zoom && WheelZoomSupported)
            Post(new { type = "mouse", buttons = (byte)(buttons & 7), dx, dy, wheel, zoom = true });
        else
            Post(new { type = "mouse", buttons = (byte)(buttons & 7), dx, dy, wheel = zoom ? 0 : wheel });
    }
    public void Keyboard(byte[] report)
    {
        if (report.Length != 8) throw new ArgumentException("Keyboard report.");
        touchModifiers = report[0];
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
    public bool Paste(string text)
    {
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
                    case "pong":
                        inputReady = root.GetProperty("ready").GetBoolean();
                        if (root.TryGetProperty("controlDisplay", out var displayValue))
                        {
                            int display = displayValue.GetInt32();long revision = root.GetProperty("controlRevision").GetInt64();
                            if (display < -1 || revision < 0) throw new InvalidDataException("Invalid control display.");
                            bool changed = controlRevision >= 0 && (controlDisplay != display || controlRevision != revision);
                            controlDisplay = display;controlRevision = revision;
                            preferWifiInput = root.GetProperty("desktopInput").GetBoolean() || display > 0;
                            if (changed)
                            {
                                touchActive = false;edge = null;
                                ControlDisplayChanged?.Invoke();
                            }
                        }
                        break;
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
                        if (touchActive && PhoneEdgeSample.TryParse(root.GetProperty("line").GetString() ?? "", Environment.TickCount64, out PhoneEdgeSample? sample) &&
                            sample!.CaptureId == epoch && sample.Zone is null)
                        {
                            PhoneEdgeSample? previous = Volatile.Read(ref edge);
                            if (previous is null || previous.CaptureId != epoch || sample.Sequence > previous.Sequence)
                                Volatile.Write(ref edge, sample);
                        }
                        break;
                    case "nativeCaptureAck":
                        if (!NativeEdgeSupported) throw new InvalidDataException("Native edge capability was not negotiated.");
                        bool ready;
                        lock (nativeEdgeGate)
                        {
                            if (nativeEpoch == 0 || root.GetProperty("epoch").GetInt32() != nativeEpoch) break;
                            ready = nativeEdgeReady = root.GetProperty("ready").GetBoolean();
                            if (!ready) nativeEdge = null;
                        }
                        NativeEdgeStatus?.Invoke(ready ? "Возврат системной мыши через край телефона включён." :
                            root.TryGetProperty("reason", out var reason) && reason.GetString() == "permission" ?
                            "На телефоне разрешите Galaxy Bridge «Поверх других приложений». Возврат: Ctrl + Alt + F12." :
                            "Край телефона недоступен. Разблокируйте экран и повторите вход. Возврат: Ctrl + Alt + F12.");
                        break;
                    case "nativeEdge":
                        if (!NativeEdgeSupported) throw new InvalidDataException("Native edge capability was not negotiated.");
                        lock (nativeEdgeGate)
                        {
                            if (PhoneEdgeSample.TryParse(root.GetProperty("line").GetString() ?? "", Environment.TickCount64, out var nativeSample) &&
                                nativeSample!.Zone is not null && nativeSample.CaptureId == nativeEpoch &&
                                nativeSample.Sequence > nativeSequence)
                            { nativeSequence = nativeSample.Sequence; nativeEdge = nativeSample; }
                        }
                        break;
                    case "nativeEdgeLeave":
                        if (!NativeEdgeSupported) throw new InvalidDataException("Native edge capability was not negotiated.");
                        lock (nativeEdgeGate)
                        {
                            long leaveSequence = root.GetProperty("sequence").GetInt64();
                            if (nativeEpoch > 0 && root.GetProperty("epoch").GetInt32() == nativeEpoch && leaveSequence > nativeSequence)
                            { nativeSequence = leaveSequence; nativeEdge = null; }
                        }
                        break;
                    case "ack":
                        string id = root.GetProperty("id").GetString() ?? "";
                        if (replies.TryRemove(id, out var completion))
                        {
                            if (root.GetProperty("ok").GetBoolean()) completion.TrySetResult(root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "");
                            else completion.TrySetException(new IOException("Телефон не сохранил файл. Проверьте свободное место и повторите отправку."));
                        }
                        break;
                    case "dragPrepare": case "dragReady": case "dragCancel":
                        // 0.7.20/0.7.21 emitted uncorrelated cancellation on release.
                        // Accept only that legacy cancel; malformed preparation/ready
                        // requests must still fail validation. Batch scoping is unchanged.
                        bool correlatedDrag = root.TryGetProperty("id", out JsonElement dragId);
                        string? dragRequest = correlatedDrag ? dragId.GetString() : null;
                        if (correlatedDrag ? !LanProtocol.Identifier(dragRequest ?? "") :
                            root.GetProperty("type").GetString() != "dragCancel" || !LanProtocol.Identifier(root.GetProperty("batch").GetString() ?? ""))
                            throw new InvalidDataException("Invalid drag request.");
                        try
                        {
                            lock (receiveGate)
                            {
                                if (!receiving.Enabled) throw new IOException("Включите приём файлов в окне файлов на ПК.");
                                string batch = root.GetProperty("batch").GetString() ?? "";
                                if (!fileEdgeEnabled && root.GetProperty("type").GetString() != "dragCancel")
                                    throw new IOException("Включите «Перенос файлов с телефона через край» в окне Galaxy Bridge на ПК.");
                                switch (root.GetProperty("type").GetString())
                                {
                                    case "dragPrepare": dragFiles.Begin(batch, root.GetProperty("count").GetInt32()); break;
                                    case "dragReady": dragFiles.Ready(batch); break;
                                    case "dragCancel": dragFiles.Cancel(batch); break;
                                }
                            }
                            if (dragRequest is not null) Post(new { type = "uploadAck", id = dragRequest, ok = true });
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                        { if (dragRequest is not null) Post(new { type = "uploadAck", id = dragRequest, ok = false, error = ex.Message }); }
                        break;
                    case "dragStart":
                        string dragBatch = root.GetProperty("batch").GetString() ?? "";
                        if (!LanProtocol.Identifier(dragBatch)) throw new InvalidDataException("Invalid drag batch.");
                        bool prepared;
                        lock (receiveGate) prepared = receiving.Enabled && fileEdgeEnabled && dragFiles.IsReady(dragBatch);
                        if (prepared && touchActive && PhoneEdgeSample.TryParse(root.GetProperty("line").GetString() ?? "", Environment.TickCount64, out var dragSample) &&
                            dragSample!.CaptureId == epoch && FileDragRequested is not null)
                            FileDragRequested.Invoke(dragBatch, dragSample);
                        else Post(new { type = "dragResult", batch = dragBatch, ok = false, error = "Подготовьте файлы и перетащите их мышью в режиме Wi-Fi." });
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
                                if (root.TryGetProperty("dragBatch", out _))
                                {
                                    if (!fileEdgeEnabled) throw new IOException("Перенос через край выключен.");
                                    if (!options.Enabled) throw new IOException("Приём файлов выключен.");
                                    saved = dragFiles.Accept(root);
                                }
                                else saved = incoming.Accept(root, options.Folder, options.Enabled);
                                if (root.GetProperty("type").GetString() == "uploadBegin")
                                    progressStaging = root.TryGetProperty("dragBatch", out _);
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
            lock (receiveGate) dragFiles.Dispose();
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
        try { while (!lifetime.IsCancellationRequested) { lock (receiveGate) { incoming.Expire(Environment.TickCount64); dragFiles.Expire(Environment.TickCount64); } Post(new { type = "ping" }); await Task.Delay(3000, lifetime.Token); } }
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
    public async Task<string> SendFileAsync(string path, CancellationToken ct, IProgress<(long Sent, long Total)>? progress = null)
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
            progress?.Report((0, size));
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[LanProtocol.ChunkSize]; int seq = 0; long sent = 0;
            int length;
            while ((length = await file.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer, 0, length); sent += length;
                if (sent > size) throw new IOException("Файл изменился во время отправки.");
                string data = Convert.ToBase64String(buffer, 0, length);
                await RequestAsync(id => new { type = "fileChunk", id, transfer, seq, data }, ct); seq++;
                progress?.Report((sent, size));
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
