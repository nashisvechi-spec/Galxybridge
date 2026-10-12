using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using GalaxyBridge.Core;
using GalaxyBridge.Windows;

internal static class LanHostTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("LAN host: checking aborted reconnects over real pinned TLS");
        string secret = "SECRET packet/QR/path must never be shown";
        string generic = LanConnectionFailure.Describe("TLS", new IOException(secret));
        check(generic.Contains("0x80131620") && generic.Contains("автоматическое переподключение"), "generic TLS I/O explains reconnect without claiming a specific cause");
        check(!generic.Contains(secret), "TLS I/O diagnostic omits provider messages");
        SocketException reset = new(10054);
        string native = LanConnectionFailure.Describe("TLS", new IOException(secret, reset));
        check(native.Contains("SocketException") && native.Contains($"0x{reset.NativeErrorCode:X8}"), "nested reset retains platform socket code");
        string auth = LanConnectionFailure.Describe("TLS", new AuthenticationException(secret, new IOException(secret)));
        check(auth.Contains("проверить TLS") && !auth.Contains("автоматическое") && !auth.Contains(secret), "authentication failure is distinct from retryable I/O");
        check(LanConnectionFailure.Describe("TLS", new OperationCanceledException(secret)).Contains("автоматическое"), "handshake timeout explains reconnect");

        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(25));
        CancellationToken ct = deadline.Token;
        await using LanHost host = new(new Settings());
        Channel<LanSession> connected = Channel.CreateUnbounded<LanSession>();
        ConcurrentQueue<string> states = new();
        TaskCompletionSource<string> interrupted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Connected += session => connected.Writer.TryWrite(session);
        host.State += text => { states.Enqueue(text); if (text.StartsWith("Попытка подключения прервана", StringComparison.Ordinal)) interrupted.TrySetResult(text); };
        host.Start();
        using (TcpClient aborted = new()) await aborted.ConnectAsync(IPAddress.Loopback, LanProtocol.Port, ct);
        check((await interrupted.Task.WaitAsync(ct)).Contains("автоматическое переподключение"), "real aborted TLS handshake reports automatic retry");
        string qr = host.NewQr(IPAddress.Loopback);
        string ticket = new Uri(qr).Query.TrimStart('?').Split('&').Single(s => s.StartsWith("ticket=", StringComparison.Ordinal))[7..];
        string device = Guid.NewGuid().ToString("N");

        using Peer first = await Peer.OpenAsync(host.Pin, ct);
        await first.SendAsync(new { type = "pair", v = 1, device, name = "Test phone", ticket, fileEdgeConfig = 1 }, ct);
        string token;
        using (JsonDocument welcome = await first.ReadAsync(ct))
        {
            token = welcome.RootElement.GetProperty("token").GetString()!;
            check(welcome.RootElement.GetProperty("fileDropZone").GetInt32() == 1 &&
                welcome.RootElement.GetProperty("fileEdge").GetInt32() == 1, "welcome advertises inset zone and legacy file edge capabilities");
            check(welcome.RootElement.GetProperty("fileEdgeConfig").GetInt32() == 1 &&
                !welcome.RootElement.GetProperty("fileEdgeEnabled").GetBoolean(), "negotiated file edge is explicitly off by default in welcome");
        }
        await first.SendAsync(new { type = "saved" }, ct);
        LanSession original = await connected.Reader.ReadAsync(ct);
        using (JsonDocument config = await first.ReadAsync(ct))
            check(config.RootElement.GetProperty("type").GetString() == "fileEdgeConfig" && !config.RootElement.GetProperty("enabled").GetBoolean(), "new phone receives persisted edge preference after saved acknowledgement");
        check(host.IsCurrent(original), "first acknowledged connection is current");
        states.Clear();

        using (Peer pending = await Peer.OpenAsync(host.Pin, ct))
        {
            await pending.SendAsync(new { type = "hello", v = 1, device, name = "Test phone", token }, ct);
            using JsonDocument welcome = await pending.ReadAsync(ct);
            check(welcome.RootElement.GetProperty("type").GetString() == "welcome", "reconnect passes authorization");
            check(original.IsAlive && host.IsCurrent(original), "working link survives while replacement awaits saved");
            await pending.SendAsync(new { type = "not-saved" }, ct);
            check(await pending.EndedAsync(ct), "unacknowledged reconnect is closed");
        }
        check(original.IsAlive && host.IsCurrent(original), "abandoned replacement leaves working link alive");
        check(states.IsEmpty && !connected.Reader.TryRead(out _), "failed parallel attempt cannot replace connected status or emit Connected");
        // The original socket still exchanges application data after that failure.
        using (JsonDocument ping = await first.ReadAsync(ct))
            check(ping.RootElement.GetProperty("type").GetString() == "ping", "original connection still receives heartbeat");
        await first.SendAsync(new { type = "pong", ready = true }, ct);

        using Peer replacement = await Peer.OpenAsync(host.Pin, ct);
        await replacement.SendAsync(new { type = "hello", v = 1, device, name = "Test phone", token }, ct);
        using (JsonDocument welcome = await replacement.ReadAsync(ct))
            check(welcome.RootElement.GetProperty("type").GetString() == "welcome", "later reconnect succeeds");
        check(original.IsAlive, "old connection remains until valid acknowledgement");
        await replacement.SendAsync(new { type = "saved" }, ct);
        LanSession renewed = await connected.Reader.ReadAsync(ct);
        check(!original.IsAlive && host.IsCurrent(renewed), "valid saved atomically promotes replacement and closes old link");

        using (Peer paused = await Peer.OpenAsync(host.Pin, ct))
        {
            await paused.SendAsync(new { type = "hello", v = 1, device, name = "Test phone", token }, ct);
            using JsonDocument welcome = await paused.ReadAsync(ct);
            host.Pause();
            await paused.SendAsync(new { type = "saved" }, ct);
            check(await paused.EndedAsync(ct) && !connected.Reader.TryRead(out _), "Pause during saved wait prevents late activation");
        }
        host.Resume();
        using (Peer forgotten = await Peer.OpenAsync(host.Pin, ct))
        {
            await forgotten.SendAsync(new { type = "hello", v = 1, device, name = "Test phone", token }, ct);
            using JsonDocument welcome = await forgotten.ReadAsync(ct);
            host.Forget();
            await forgotten.SendAsync(new { type = "saved" }, ct);
            check(await forgotten.EndedAsync(ct) && !connected.Reader.TryRead(out _), "Forget during saved wait prevents stale authorization activation");
        }
    }

    private sealed class Peer(TcpClient client, SslStream stream) : IDisposable
    {
        public static async Task<Peer> OpenAsync(string pin, CancellationToken ct)
        {
            TcpClient client = new(); SslStream? stream = null;
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, LanProtocol.Port, ct);
                stream = new SslStream(client.GetStream(), false, (_, certificate, _, _) => certificate is not null &&
                    Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).Equals(pin, StringComparison.OrdinalIgnoreCase));
                await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                { TargetHost = "Galaxy Bridge host test", EnabledSslProtocols = SslProtocols.Tls12 }, ct);
                return new(client, stream);
            }
            catch { stream?.Dispose(); client.Dispose(); throw; }
        }
        public ValueTask SendAsync(object message, CancellationToken ct) => stream.WriteAsync(LanProtocol.Encode(message), ct);
        public Task<JsonDocument> ReadAsync(CancellationToken ct) => LanProtocol.ReadAsync(stream, ct);
        public async Task<bool> EndedAsync(CancellationToken ct) => await stream.ReadAsync(new byte[1], ct) == 0;
        public void Dispose() { stream.Dispose(); client.Dispose(); }
    }
}
