namespace GalaxyBridge.Core;

// Serialize notifications to one explicitly selected host. Ending capture
// invalidates queued presses/motion, then sends neutral reports to that host.
public sealed class HidReportPump : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Queue<Entry> queue = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<string, byte, byte[], CancellationToken, Task> send;
    private readonly Task writer;
    private string? target;
    private long generation;
    private bool active, failed, disposed;
    public event Action<string>? Fault;
    public bool IsActive { get { lock (gate) return active && !failed && !disposed; } }
    public bool Failed { get { lock (gate) return failed; } }
    private sealed record Entry(string Target, byte Report, byte[] Payload, long Generation, bool Release, TaskCompletionSource? Barrier = null);
    public HidReportPump(Func<string, byte, byte[], CancellationToken, Task> send)
    { this.send = send; writer = RunAsync(); }
    public void Begin(string selectedHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedHost);
        lock (gate)
        {
            if (disposed || failed) throw new InvalidOperationException("Канал Bluetooth-ввода недоступен. Отключите и включите системную мышь.");
            if (active) throw new InvalidOperationException("Сначала верните управление на ПК.");
            target = selectedHost; generation++; active = true;
        }
    }
    public bool Post(byte report, byte[] payload)
    {
        int length = report == 1 ? 8 : report == 2 ? Hid.MouseReportLength : 0;
        if (length == 0 || payload.Length != length) throw new ArgumentException("Invalid HID report.");
        string? failure = null;
        lock (gate)
        {
            if (!active || failed || disposed || target is null) return false;
            if (queue.Count >= 512) { failed = true; StopLocked(); failure = "Очередь Bluetooth переполнена. Управление остановлено."; }
            else Enqueue(new(target, report, (byte[])payload.Clone(), generation, false));
        }
        if (failure is null) return true;
        Fault?.Invoke(failure); return false;
    }
    public void Stop() { lock (gate) { if (!disposed) StopLocked(); } }
    private void StopLocked()
    {
        active = false; generation++;
        // Releases/barriers for a previous target must survive a quick switch.
        Entry[] keep = queue.Where(e => e.Release || e.Barrier is not null).ToArray();
        queue.Clear(); foreach (Entry e in keep) queue.Enqueue(e);
        if (target is not null)
        {
            Enqueue(new(target, 1, new byte[8], generation, true));
            Enqueue(new(target, 2, new byte[Hid.MouseReportLength], generation, true));
            target = null;
        }
    }
    private void Enqueue(Entry entry) { queue.Enqueue(entry); signal.Release(); }
    public Task FlushAsync()
    {
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            TaskCompletionSource barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(new("", 0, [], generation, true, barrier));return barrier.Task;
        }
    }
    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await signal.WaitAsync(lifetime.Token).ConfigureAwait(false);
                Entry entry;
                lock (gate)
                {
                    if (queue.Count == 0) continue;
                    entry = queue.Dequeue();
                    if (!entry.Release && entry.Generation != generation) continue;
                }
                if (entry.Barrier is not null) { entry.Barrier.TrySetResult();continue; }
                try { await send(entry.Target, entry.Report, entry.Payload, lifetime.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException || !lifetime.IsCancellationRequested)
                {
                    bool notify;
                    lock (gate) { notify = !failed;failed = true; if (notify) StopLocked(); }
                    if (notify) Fault?.Invoke("Bluetooth-ввод прерван: " + ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        Stop();
        try { await FlushAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (TimeoutException) { }
        lock (gate) { if (disposed) return;disposed = true; }
        lifetime.Cancel();
        try { await writer.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (TimeoutException) { }
        if (writer.IsCompleted) { signal.Dispose();lifetime.Dispose(); }
        else _ = writer.ContinueWith(_ => { signal.Dispose();lifetime.Dispose(); }, TaskScheduler.Default);
    }
}
