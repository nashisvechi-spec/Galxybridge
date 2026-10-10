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
        await using var peer = await Peer.CreateAsync();
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
    private sealed class Peer : IAsyncDisposable
    {
        private readonly TcpClient client;
        private readonly X509Certificate2 certificate;
        public SslStream Stream { get; }
        public LanSession Session { get; }
        private Peer(TcpClient client, SslStream stream, LanSession session, X509Certificate2 certificate)
        { this.client = client; Stream = stream; Session = session; this.certificate = certificate; }
        public static async Task<Peer> CreateAsync()
        {
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new("CN=Galaxy Bridge clipboard test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            string pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
            TcpListener listener = new(IPAddress.Loopback, 0); listener.Start();
            TcpClient client = new();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            TcpClient accepted = await listener.AcceptTcpClientAsync(); listener.Stop();
            SslStream server = new(accepted.GetStream(), false);
            SslStream stream = new(client.GetStream(), false, (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == pin);
            await Task.WhenAll(server.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 }),
                stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12 }));
            LanSession session = new(accepted, server, "clipboard test"); session.Start();
            return new(client, stream, session, certificate);
        }
        public async Task<JsonDocument> RequestAsync(string id, string text)
        {
            await Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id, text }));
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(8));
            while (true)
            {
                JsonDocument reply = await LanProtocol.ReadAsync(Stream, deadline.Token);
                if (reply.RootElement.GetProperty("type").GetString() != "ping") return reply;
                reply.Dispose(); await Stream.WriteAsync(LanProtocol.Encode(new { type = "pong", ready = false }));
            }
        }
        public async ValueTask DisposeAsync()
        { Session.Close(); await Session.Completion; Stream.Dispose(); client.Dispose(); certificate.Dispose(); }
    }
}
