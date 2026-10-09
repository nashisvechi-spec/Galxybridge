using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed class PhoneSession : IAsyncDisposable
{
    private readonly AdbClient adb;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<Outgoing> outgoing = Channel.CreateBounded<Outgoing>(
        new BoundedChannelOptions(512) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private TcpClient? socket;
    private Process? server;
    private Task? writer, reader;
    private EdgeFeedback? edgeFeedback;
    private int port;
    private string remoteJar = "";
    private int disposed, failureNotified;
    private readonly Action<string> log;
    public string Serial { get; }
    public string DeviceName { get; private set; } = "Galaxy";
    public bool IsAlive => socket is not null && !lifetime.IsCancellationRequested && Volatile.Read(ref disposed) == 0;
    public event Action<string>? ClipboardReceived;
    public event Action<string>? ConnectionLost;
    public event Action<string>? AutoReturnUnavailable;
    public bool AutoReturnReady => edgeFeedback?.Ready == true;
    public PhoneEdgeSample? LatestEdge => edgeFeedback?.Latest;
    public int BeginEdgeReturn(PhoneSide side) => edgeFeedback?.Begin(side) ?? 0;
    public void EndEdgeReturn() => edgeFeedback?.End();

    private sealed record Outgoing(byte[]? Packet, TaskCompletionSource? Barrier = null);
    public PhoneSession(AdbClient adb, string serial, Action<string> log)
    { this.adb = adb; Serial = serial; this.log = log; }

    public async Task ConnectAsync(CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        CancellationToken ct = deadline.Token;
        adb.VerifyBackend();
        string scid = RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString("x8", CultureInfo.InvariantCulture);
        remoteJar = $"/data/local/tmp/galaxybridge-{scid}.jar";
        _ = await adb.RunAsync(["-s", Serial, "push", adb.ServerPath, remoteJar], ct);
        string forwarded = await adb.RunAsync(["-s", Serial, "forward", "tcp:0", $"localabstract:scrcpy_{scid}"], ct);
        if (!int.TryParse(forwarded.Trim(), out port) || port is < 1 or > 65535)
            throw new IOException("Не удалось открыть локальное подключение.");
        server = adb.StartServer(Serial, remoteJar, scid, log);

        Stopwatch retry = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (server.HasExited) throw new IOException("Компонент управления завершился. Проверьте журнал и разрешение отладки.");
            TcpClient candidate = new() { NoDelay = true };
            try
            {
                using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(TimeSpan.FromSeconds(2));
                await candidate.ConnectAsync(IPAddress.Loopback, port, attempt.Token);
                byte[] hello = new byte[1];
                await candidate.GetStream().ReadExactlyAsync(hello, attempt.Token);
                if (hello[0] != 0) throw new InvalidDataException("Invalid scrcpy handshake.");
                byte[] name = new byte[64];
                await candidate.GetStream().ReadExactlyAsync(name, attempt.Token);
                DeviceName = Encoding.UTF8.GetString(name).TrimEnd('\0');
                socket = candidate;
                break;
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                candidate.Dispose();
                if (ct.IsCancellationRequested) throw;
                if (retry.Elapsed > TimeSpan.FromSeconds(12)) throw new IOException("Не удалось начать управление телефоном.", ex);
                await Task.Delay(150, ct);
            }
        }

        NetworkStream stream = (socket ?? throw new IOException("Соединение не открыто.")).GetStream();
        await stream.WriteAsync(ControlProtocol.CreateHid(ControlProtocol.KeyboardId, "Galaxy Bridge Keyboard", Hid.KeyboardDescriptor), ct);
        await stream.WriteAsync(ControlProtocol.CreateHid(ControlProtocol.MouseId, "Galaxy Bridge Mouse", Hid.MouseDescriptor), ct);
        writer = WriteLoopAsync(stream); reader = ReadLoopAsync(stream);
        // The optional companion has its own timeout; it must not expire basic control.
        deadline.CancelAfter(Timeout.InfiniteTimeSpan);
        edgeFeedback = new(adb, Serial, scid, log);
        edgeFeedback.Unavailable += message => AutoReturnUnavailable?.Invoke(message);
        try { await edgeFeedback.StartAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            log("Автовозврат недоступен: " + ex.Message);
            await edgeFeedback.DisposeAsync(); edgeFeedback = null;
        }
    }

    public bool Send(byte[] packet)
    {
        if (Volatile.Read(ref disposed) != 0 || lifetime.IsCancellationRequested) return false;
        if (outgoing.Writer.TryWrite(new Outgoing(packet))) return true;
        Fail(new IOException("Очередь команд переполнена.")); // Never silently drop a key-up.
        return false;
    }
    public void Keyboard(byte[] report) => Send(ControlProtocol.HidInput(ControlProtocol.KeyboardId, report));
    public void Mouse(byte buttons, int dx, int dy, int wheel = 0)
    {
        foreach (byte[] report in Hid.MouseReports(buttons, dx, dy, wheel))
            Send(ControlProtocol.HidInput(ControlProtocol.MouseId, report));
    }
    public void ReleaseInputs() { Keyboard(new byte[8]); Mouse(0, 0, 0); }
    public void AndroidKey(int keycode)
    { Send(ControlProtocol.Keycode(keycode, true)); Send(ControlProtocol.Keycode(keycode, false)); }

    private async Task WriteLoopAsync(NetworkStream stream)
    {
        try
        {
            await foreach (Outgoing message in outgoing.Reader.ReadAllAsync(lifetime.Token))
            {
                if (message.Packet is not null) await stream.WriteAsync(message.Packet, lifetime.Token);
                message.Barrier?.TrySetResult();
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        { Fail(ex); }
    }

    private async Task ReadLoopAsync(NetworkStream stream)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                DeviceMessage message = await ControlProtocol.ReadDeviceMessageAsync(stream, lifetime.Token);
                if (message.ClipboardText is not null) ClipboardReceived?.Invoke(message.ClipboardText);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        { Fail(ex); }
    }
    private void Fail(Exception error)
    {
        if (Volatile.Read(ref disposed) != 0 || lifetime.IsCancellationRequested) return;
        if (Interlocked.Exchange(ref failureNotified, 1) == 0)
        {
            log("Потеря подключения: " + error.GetType().Name);
            ConnectionLost?.Invoke("Связь с телефоном прервалась. Управление возвращено ноутбуку.");
        }
        lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (edgeFeedback is not null) { await edgeFeedback.DisposeAsync(); edgeFeedback = null; }
        // Queue releases directly before shutting down the stream; Send() is now closed.
        if (writer is not null && !lifetime.IsCancellationRequested)
        {
            outgoing.Writer.TryWrite(new Outgoing(ControlProtocol.HidInput(ControlProtocol.KeyboardId, new byte[8])));
            outgoing.Writer.TryWrite(new Outgoing(ControlProtocol.HidInput(ControlProtocol.MouseId, new byte[4])));
            outgoing.Writer.TryWrite(new Outgoing(ControlProtocol.DestroyHid(ControlProtocol.KeyboardId)));
            outgoing.Writer.TryWrite(new Outgoing(ControlProtocol.DestroyHid(ControlProtocol.MouseId)));
            TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
            outgoing.Writer.TryWrite(new Outgoing(null, barrier));
            try { await barrier.Task.WaitAsync(TimeSpan.FromMilliseconds(500)); } catch (TimeoutException) { }
        }
        outgoing.Writer.TryComplete(); lifetime.Cancel(); socket?.Dispose();
        if (server is not null)
        {
            try { if (!server.HasExited) server.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            { log("Локальный процесс подключения уже завершился или недоступен."); }
            server.Dispose();
        }
        try { await Task.WhenAll(writer ?? Task.CompletedTask, reader ?? Task.CompletedTask); } catch (OperationCanceledException) { }
        using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(4));
        try
        {
            if (port > 0) _ = await adb.RunAsync(["-s", Serial, "forward", "--remove", $"tcp:{port}"], cleanup.Token);
            if (remoteJar.Length > 0) _ = await adb.RunAsync(["-s", Serial, "shell", "rm", "-f", remoteJar], cleanup.Token);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or System.ComponentModel.Win32Exception)
        { log("Подключение закрыто; телефон недоступен для очистки."); }
        lifetime.Dispose();
    }
}
