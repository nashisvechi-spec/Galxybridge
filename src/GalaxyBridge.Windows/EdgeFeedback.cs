using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed class EdgeFeedback : IAsyncDisposable
{
    private readonly AdbClient adb;
    private readonly string serial, socketName;
    private readonly Action<string> log;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<string> commands = Channel.CreateUnbounded<string>(new() { SingleReader = true });
    private TcpClient? socket;
    private StreamReader? input;
    private StreamWriter? output;
    private Task? reader, writer, heartbeat;
    private PhoneEdgeSample? sample;
    private int port, epoch, nextEpoch, disposed, reported, state;
    private long lastSequence;
    public bool Ready => Volatile.Read(ref state) == 1;
    public PhoneEdgeSample? Latest => Volatile.Read(ref sample);
    public event Action<string>? Unavailable;

    public EdgeFeedback(AdbClient adb, string serial, string scid, Action<string> log)
    { this.adb = adb; this.serial = serial; this.log = log; socketName = "galaxybridge_edge_" + scid; }

    public async Task StartAsync(CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try { await StartCoreAsync(deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Galaxy Bridge Edge не ответил за 12 секунд. Проверьте APK и разрешение поверх других приложений."); }
    }

    private async Task StartCoreAsync(CancellationToken token)
    {
        const string install = "Для автовозврата установите GalaxyBridgeEdge.apk на S25, откройте Galaxy Bridge Edge и разрешите показ поверх других приложений.";
        string installed;
        try { installed = await adb.RunAsync(["-s", serial, "shell", "pm", "path", "com.galaxybridge.edge"], token); }
        catch (IOException ex) { throw new IOException(install, ex); }
        if (!installed.Contains("package:", StringComparison.Ordinal)) throw new FileNotFoundException(install);
        string forwarded = await adb.RunAsync(["-s", serial, "forward", "tcp:0", "localabstract:" + socketName], token);
        if (!int.TryParse(forwarded.Trim(), out port) || port is < 1 or > 65535) throw new IOException("Не удалось открыть канал автовозврата.");
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        string launch = await adb.RunAsync(["-s", serial, "shell", "am", "start-foreground-service", "-n", "com.galaxybridge.edge/.EdgeService",
            "--es", "socket", socketName, "--es", "token", secret], token);
        if (launch.Contains("Error", StringComparison.OrdinalIgnoreCase) || launch.Contains("Exception", StringComparison.Ordinal))
            throw new IOException("Не удалось запустить Galaxy Bridge Edge. " + install);

        Stopwatch retry = Stopwatch.StartNew();
        string? hello;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            TcpClient candidate = new() { NoDelay = true };
            try
            {
                using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                attempt.CancelAfter(TimeSpan.FromSeconds(6));
                await candidate.ConnectAsync(IPAddress.Loopback, port, attempt.Token);
                NetworkStream stream = candidate.GetStream();
                StreamWriter outgoing = new(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
                StreamReader incoming = new(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                await outgoing.WriteLineAsync(("HELLO 3 " + secret).AsMemory(), attempt.Token);
                await outgoing.FlushAsync(attempt.Token);
                hello = await incoming.ReadLineAsync(attempt.Token);
                if (hello is null) throw new IOException("Компонент автовозврата ещё не доступен.");
                socket = candidate; input = incoming; output = outgoing; break;
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                candidate.Dispose();
                if (token.IsCancellationRequested) throw;
                if (retry.Elapsed > TimeSpan.FromSeconds(8)) throw new IOException("Galaxy Bridge Edge не ответил. " + install, ex);
                await Task.Delay(150, token);
            }
        }
        string greeting = hello ?? throw new IOException("Компонент автовозврата не ответил.");
        if (greeting != "GB_EDGE_READY 3")
            throw new IOException(greeting.StartsWith("GB_EDGE_ERROR ", StringComparison.Ordinal)
                ? "Ошибка компонента автовозврата: " + greeting[14..].Trim() : "Обновите Galaxy Bridge Edge на телефоне до версии 0.3.0.");
        Interlocked.Exchange(ref state, 1);
        reader = ReadAsync(); writer = WriteAsync(); heartbeat = HeartbeatAsync();
        log("Galaxy Bridge Edge подключён. Зона включается только во время управления.");
    }
    public int Begin(PhoneSide side)
    {
        if (!Ready) return 0;
        int id = Interlocked.Increment(ref nextEpoch);
        Volatile.Write(ref sample, null); Interlocked.Exchange(ref lastSequence, 0);
        Volatile.Write(ref epoch, id); commands.Writer.TryWrite($"START {id} {(int)side}"); return id;
    }
    public void End()
    {
        int previous = Interlocked.Exchange(ref epoch, 0); Volatile.Write(ref sample, null);
        if (previous > 0) commands.Writer.TryWrite($"STOP {previous}");
    }
    private async Task ReadAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                string? line = await input!.ReadLineAsync(lifetime.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(7), lifetime.Token).ConfigureAwait(false);
                if (line is null) throw new IOException("Galaxy Bridge Edge завершил подключение.");
                if (line.StartsWith("GB_EDGE_ERROR ", StringComparison.Ordinal)) throw new IOException(line[14..].Trim());
                if (line.StartsWith("GB_EDGE_INFO ", StringComparison.Ordinal)) log("Автовозврат: " + line[13..].Trim());
                else if (line.StartsWith("GB_EDGE_ACTIVE ", StringComparison.Ordinal) && int.TryParse(line[15..], out int active) && active == Volatile.Read(ref epoch))
                    log("Зона автовозврата создана на телефоне.");
                else if (PhoneEdgeSample.TryParse(line, Environment.TickCount64, out PhoneEdgeSample? parsed) && parsed is not null &&
                    parsed.CaptureId == Volatile.Read(ref epoch) && parsed.Sequence > Interlocked.Read(ref lastSequence))
                { Interlocked.Exchange(ref lastSequence, parsed.Sequence); Volatile.Write(ref sample, parsed); }
                else if (line.StartsWith("GB_EDGE_LEFT ", StringComparison.Ordinal) && int.TryParse(line[13..], out int left) && left == Volatile.Read(ref epoch))
                    Volatile.Write(ref sample, null);
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException) { ReportFailure(ex); }
    }
    private async Task WriteAsync()
    {
        try
        {
            await foreach (string command in commands.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            { await output!.WriteLineAsync(command.AsMemory(), lifetime.Token).ConfigureAwait(false); await output.FlushAsync(lifetime.Token).ConfigureAwait(false); }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { ReportFailure(ex); }
    }
    private async Task HeartbeatAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            { commands.Writer.TryWrite("PING"); await Task.Delay(1000, lifetime.Token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) { }
    }
    private void ReportFailure(Exception ex)
    {
        Interlocked.Exchange(ref state, 2); Volatile.Write(ref sample, null); socket?.Dispose(); lifetime.Cancel();
        if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref reported, 1) != 0) return;
        string message = "Автовозврат недоступен. Используйте Ctrl + Alt + F12. " + ex.Message;
        log(message); Unavailable?.Invoke(message);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        End(); Interlocked.Exchange(ref state, 3); commands.Writer.TryWrite("QUIT"); commands.Writer.TryComplete();
        try { if (writer is not null) await writer.WaitAsync(TimeSpan.FromMilliseconds(500)); } catch (TimeoutException) { }
        lifetime.Cancel(); socket?.Dispose();
        try { await Task.WhenAll(reader ?? Task.CompletedTask, writer ?? Task.CompletedTask, heartbeat ?? Task.CompletedTask); } catch (OperationCanceledException) { }
        try { input?.Dispose(); output?.Dispose(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(3));
        try { if (port > 0) _ = await adb.RunAsync(["-s", serial, "forward", "--remove", $"tcp:{port}"], cleanup.Token); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception) { }
        lifetime.Dispose();
    }
}
