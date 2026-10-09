using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Channels;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed class EdgeFeedback : IAsyncDisposable
{
    private readonly AdbClient adb;
    private readonly string serial, remoteJar;
    private readonly Action<string> log;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<string> commands = Channel.CreateUnbounded<string>(new() { SingleReader = true });
    private readonly TaskCompletionSource readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? process;
    private Task? reader, writer, errors;
    private PhoneEdgeSample? sample;
    private int epoch, nextEpoch, disposed, reported, state;
    private long lastSequence;
    public bool Ready => Volatile.Read(ref state) == 1;
    public PhoneEdgeSample? Latest => Volatile.Read(ref sample);
    public event Action<string>? Unavailable;

    public EdgeFeedback(AdbClient adb, string serial, string scid, Action<string> log)
    { this.adb = adb; this.serial = serial; this.log = log; remoteJar = $"/data/local/tmp/galaxybridge-edge-{scid}.jar"; }

    public async Task StartAsync(CancellationToken token)
    {
        string jar = Path.Combine(adb.BackendDirectory, "edge-return.jar");
        string digest = Path.Combine(adb.BackendDirectory, "edge-return.sha256");
        if (!File.Exists(jar) || !File.Exists(digest))
            throw new FileNotFoundException("Для автовозврата нужна новая полная сборка с backend/edge-return.jar.");
        string expected = File.ReadAllText(digest).Trim();
        if (expected.Length != 64 || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jar))).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Компонент автовозврата повреждён или не соответствует своему SHA-256.");
        _ = await adb.RunAsync(["-s", serial, "push", jar, remoteJar], token);
        process = adb.StartEdgeHelper(serial, remoteJar);
        reader = ReadAsync(process); writer = WriteAsync(process);
        errors = DrainErrorsAsync(process);
        await readySignal.Task.WaitAsync(TimeSpan.FromSeconds(8), token);
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
            throw new IOException("Компонент автовозврата завершился при подключении.");
        log("Компонент автовозврата готов. Зона на телефоне включается только во время управления.");
    }

    public int Begin(PhoneSide side)
    {
        if (!Ready) return 0;
        int id = Interlocked.Increment(ref nextEpoch);
        Volatile.Write(ref sample, null); Interlocked.Exchange(ref lastSequence, 0);
        Volatile.Write(ref epoch, id);
        commands.Writer.TryWrite($"START {id} {(int)side}");
        return id;
    }
    public void End()
    {
        int previous = Interlocked.Exchange(ref epoch, 0);
        Volatile.Write(ref sample, null);
        if (previous > 0) commands.Writer.TryWrite($"STOP {previous}");
    }
    private async Task ReadAsync(Process child)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                string? line = await child.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                if (line is null) throw new IOException("Компонент автовозврата завершился.");
                if (line == "GB_EDGE_READY 1") readySignal.TrySetResult();
                else if (line.StartsWith("GB_EDGE_ERROR ", StringComparison.Ordinal))
                    throw new IOException("Android не разрешил зону автовозврата: " + line[14..].Trim());
                else if (PhoneEdgeSample.TryParse(line, Environment.TickCount64, out PhoneEdgeSample? parsed) && parsed is not null &&
                         parsed.CaptureId == Volatile.Read(ref epoch) && parsed.Sequence > Interlocked.Read(ref lastSequence))
                { Interlocked.Exchange(ref lastSequence, parsed.Sequence); Volatile.Write(ref sample, parsed); }
                else if (line.StartsWith("GB_EDGE_LEFT ", StringComparison.Ordinal) &&
                         int.TryParse(line[13..], out int left) && left == Volatile.Read(ref epoch)) Volatile.Write(ref sample, null);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        { ReportFailure(ex); }
    }
    private async Task WriteAsync(Process child)
    {
        try
        {
            await foreach (string command in commands.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            { await child.StandardInput.WriteLineAsync(command.AsMemory(), lifetime.Token).ConfigureAwait(false); await child.StandardInput.FlushAsync(lifetime.Token).ConfigureAwait(false); }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        { ReportFailure(ex); }
    }
    private async Task DrainErrorsAsync(Process child)
    {
        try { _ = await child.StandardError.ReadToEndAsync(lifetime.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    private void ReportFailure(Exception ex)
    {
        Interlocked.Exchange(ref state, 2); Volatile.Write(ref sample, null);
        if (Volatile.Read(ref disposed) != 0 || lifetime.IsCancellationRequested) { readySignal.TrySetCanceled(); return; }
        readySignal.TrySetException(ex);
        if (Interlocked.Exchange(ref reported, 1) != 0) return;
        string message = "Автовозврат недоступен. Используйте Ctrl + Alt + F12. " + ex.Message;
        log(message); Unavailable?.Invoke(message);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        End(); Interlocked.Exchange(ref state, 3); commands.Writer.TryWrite("QUIT"); commands.Writer.TryComplete();
        Process? child = process;
        if (child is not null)
        {
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(700)); }
            catch (TimeoutException) { }
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        lifetime.Cancel();
        try { await Task.WhenAll(reader ?? Task.CompletedTask, writer ?? Task.CompletedTask, errors ?? Task.CompletedTask); }
        catch (OperationCanceledException) { }
        child?.Dispose();
        using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(3));
        try { _ = await adb.RunAsync(["-s", serial, "shell", "rm", "-f", remoteJar], cleanup.Token); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or System.ComponentModel.Win32Exception) { }
        lifetime.Dispose();
    }
}
