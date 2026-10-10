using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using GalaxyBridge.Core;
using GalaxyBridge.Windows;

static class LanClipboardTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await HandshakeFailureTestsAsync(check);
        Console.WriteLine("LAN clipboard: creating TLS test connection (10 second deadline)");
        await using var peer = await Peer.CreateAsync();
        Console.WriteLine("LAN clipboard: TLS ready; checking delivery and rejection");
        string? copied = null; int writes = 0;
        peer.Session.ConfigureClipboard(true);
        peer.Session.ClipboardWriter = (text, ct) => { copied = text; writes++; return Task.FromResult(true); };
        foreach (string text in new[] { "Текст с телефона 🌍\n", "https://example.com/?q=икона&n=2" })
        {
            string id = Guid.NewGuid().ToString("N");
            using JsonDocument reply = await peer.RequestAsync(id, text);
            check(reply.RootElement.GetProperty("type").GetString() == "clipboardAck", "actual LAN session acknowledges clipboard packet");
            check(reply.RootElement.GetProperty("id").GetString() == id, "clipboard acknowledgement identifies the request");
            check(reply.RootElement.GetProperty("ok").GetBoolean() && copied == text, "clipboard success follows exact Unicode text delivery");
        }
        int before = writes; peer.Session.ConfigureClipboard(false);
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "must not replace clipboard"))
            check(!reply.RootElement.GetProperty("ok").GetBoolean() && writes == before, "disabled clipboard rejects without writing");
        peer.Session.ConfigureClipboard(true);
        peer.Session.ClipboardWriter = (text, ct) => Task.FromResult(false);
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "busy clipboard"))
            check(!reply.RootElement.GetProperty("ok").GetBoolean() && peer.Session.IsAlive, "busy clipboard returns failure without breaking connection");
        peer.Session.ClipboardWriter = async (text, ct) => { await Task.Delay(10000, ct); return true; };
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "timed out clipboard"))
            check(!reply.RootElement.GetProperty("ok").GetBoolean() && peer.Session.IsAlive, "clipboard deadline rejects delayed UI write without disconnecting");
        peer.Session.ClipboardWriter = (text, ct) => { writes++; copied = text; return Task.FromResult(true); };
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "after timeout"))
            check(reply.RootElement.GetProperty("ok").GetBoolean() && writes == before + 1 && copied == "after timeout", "timed out clipboard is not replayed and next request succeeds");
        peer.Session.ClipboardWriter = null;
        using (JsonDocument reply = await peer.RequestAsync(Guid.NewGuid().ToString("N"), "no UI receiver"))
            check(!reply.RootElement.GetProperty("ok").GetBoolean(), "missing UI receiver cannot report success");
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id = "invalid", text = "reject" }));
        await peer.Session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        check(!peer.Session.IsAlive, "malformed clipboard request closes protocol session");
    }
    // Task.WhenAll alone does not stop the client if the server fails before
    // sending its handshake. Abort both transports and cancel the sibling.
    private static async Task AuthenticateTogetherAsync(Func<CancellationToken, Task> server,
        Func<CancellationToken, Task> client, CancellationToken timeout, Action abort)
    {
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(timeout);
        async Task Authenticate(Func<CancellationToken, Task> action)
        {
            try { await action(failed.Token).WaitAsync(failed.Token); }
            catch { failed.Cancel(); abort(); throw; }
        }
        await Task.WhenAll(Authenticate(server), Authenticate(client));
    }
    private static async Task HandshakeFailureTestsAsync(Action<bool, string> check)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        TaskCompletionSource clientStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool aborted = false, rejected = false;
        try
        {
            await AuthenticateTogetherAsync(async ct =>
            {
                await clientStarted.Task.WaitAsync(ct);
                throw new IOException("Injected TLS server credential failure");
            }, ct => { clientStarted.SetResult(); return Task.Delay(Timeout.Infinite, ct); }, deadline.Token, () => aborted = true);
        }
        catch (IOException) { rejected = true; }
        check(rejected && aborted && !deadline.IsCancellationRequested, "TLS server failure aborts the waiting client before the deadline");
        using var stalledDeadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        TaskCompletionSource stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        aborted = false; bool expired = false;
        try { await AuthenticateTogetherAsync(_ => stalled.Task, _ => stalled.Task, stalledDeadline.Token, () => aborted = true); }
        catch (OperationCanceledException) { expired = true; }
        check(expired && aborted, "TLS deadline aborts even a handshake that ignores cancellation");
    }
    private sealed class Peer : IAsyncDisposable
    {
        private readonly TcpClient client;
        private readonly X509Certificate2 certificate;
        private readonly SslStream server;
        public SslStream Stream { get; }
        public LanSession Session { get; }
        private Peer(TcpClient client, SslStream stream, LanSession session, X509Certificate2 certificate, SslStream server)
        { this.client = client; Stream = stream; Session = session; this.certificate = certificate; this.server = server; }
        public static async Task<Peer> CreateAsync()
        {
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new("CN=Galaxy Bridge clipboard test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            // Match production's user key container. Schannel cannot use an
            // ephemeral private key returned directly by CreateSelfSigned.
            byte[] pfx = generated.Export(X509ContentType.Pfx);
            X509Certificate2 certificate;
            try { certificate = new(pfx, (string?)null, X509KeyStorageFlags.UserKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
            string pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
            using TcpListener listener = new(IPAddress.Loopback, 0);
            TcpClient client = new();
            TcpClient? accepted = null; SslStream? server = null, stream = null;
            string stage = "loopback connection";
            try
            {
                listener.Start(); await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token);
                accepted = await listener.AcceptTcpClientAsync(deadline.Token); listener.Stop();
                server = new(accepted.GetStream(), false);
                stream = new(client.GetStream(), false, (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == pin);
                stage = "TLS authentication";
                await AuthenticateTogetherAsync(
                    ct => server.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 }, ct),
                    ct => stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12 }, ct),
                    deadline.Token, () => { accepted.Dispose(); client.Dispose(); });
                LanSession session = new(accepted, server, "clipboard test"); session.Start();
                return new(client, stream, session, certificate, server);
            }
            catch (Exception ex)
            {
                server?.Dispose(); stream?.Dispose(); accepted?.Dispose(); client.Dispose(); certificate.Dispose();
                throw new InvalidOperationException($"LAN clipboard test setup failed during {stage} (10 second deadline).", ex);
            }
        }
        public async Task<JsonDocument> RequestAsync(string id, string text)
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(8));
            await Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id, text }), deadline.Token);
            while (true)
            {
                JsonDocument reply = await LanProtocol.ReadAsync(Stream, deadline.Token);
                if (reply.RootElement.GetProperty("type").GetString() != "ping") return reply;
                reply.Dispose(); await Stream.WriteAsync(LanProtocol.Encode(new { type = "pong", ready = false }), deadline.Token);
            }
        }
        public async ValueTask DisposeAsync()
        {
            try { Session.Close(); await Session.Completion.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { server.Dispose(); Stream.Dispose(); client.Dispose(); certificate.Dispose(); }
        }
    }
}
