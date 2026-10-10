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
        await AutomaticAsync(check);
    }
    private static async Task AutomaticAsync(Action<bool, string> check)
    {
        await using var peer = await Peer.CreateAsync(true);
        uint sequence = 10; int writes = 0; string? copied = null;
        peer.Session.ClipboardSequenceReader = () => sequence;
        peer.Session.AutomaticClipboardWriter = (text, expected, ct) =>
        {
            if (ct.IsCancellationRequested || expected != sequence) return Task.FromResult(false);
            copied = text; writes++; return Task.FromResult(true);
        };
        peer.Session.ConfigureClipboard(true);
        using (var config = await peer.NextAsync())
            check(config.RootElement.GetProperty("type").GetString() == "clipboardConfig" && config.RootElement.GetProperty("enabled").GetBoolean(), "new peer negotiates clipboard enablement");
        check(peer.Session.PushClipboard("С ПК 🌍"), "PC clipboard is queued for new peer");
        string setId;
        using (var set = await peer.NextAsync())
        {
            check(set.RootElement.GetProperty("type").GetString() == "clipboardSet" && set.RootElement.GetProperty("text").GetString() == "С ПК 🌍", "PC copy updates system clipboard, without a paste command");
            setId = set.RootElement.GetProperty("id").GetString()!;
        }
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboardSetAck", id = setId, ok = true }));
        string request = await PullAsync(peer);
        using (var ack = await AutoReplyAsync(peer, request, "С телефона 🌍"))
            check(ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1 && copied == "С телефона 🌍", "phone clipboard on return is written and acknowledged");
        using (var ack = await AutoReplyAsync(peer, request, "duplicate response"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "duplicate automatic response cannot overwrite clipboard");
        request = await PullAsync(peer); sequence++;
        using (var ack = await AutoReplyAsync(peer, request, "stale phone text"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "PC copy made during pull wins even before clipboard notification is processed");
        request = await PullAsync(peer); peer.Session.CancelClipboardPull();
        using (var ack = await AutoReplyAsync(peer, request, "cancelled response"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "cancelled pull cannot write");
        request = await PullAsync(peer); peer.Session.BeginEdgeReturn(PhoneSide.Right);
        using (var capture = await peer.NextAsync()) check(capture.RootElement.GetProperty("active").GetBoolean(), "control re-entry is ordered after pull");
        using (var ack = await AutoReplyAsync(peer, request, "late response after re-entry"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "returning to phone cancels outstanding pull");
        request = await PullAsync(peer); peer.Session.ConfigureClipboard(false);
        using (var config = await peer.NextAsync()) check(!config.RootElement.GetProperty("enabled").GetBoolean(), "disabling is sent to the phone");
        using (var ack = await AutoReplyAsync(peer, request, "disabled response"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1 && !peer.Session.PushClipboard("disabled"), "disabled shared clipboard refuses both directions");
        peer.Session.ConfigureClipboard(true); using (var config = await peer.NextAsync()) { }
        request = await PullAsync(peer);
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboardResult", request, status = "permission" }));
        // A subsequent packet serves as a barrier for the result in the same TLS stream.
        using (var ack = await AutoReplyAsync(peer, request, "after empty result"))
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "permission/empty results consume only the current request");
        request = await PullAsync(peer);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.Session.AutomaticClipboardWriter = async (text, expected, ct) =>
        { started.SetResult(); await Task.Delay(10000, ct); writes++; return true; };
        string pendingId = Guid.NewGuid().ToString("N");
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id = pendingId, request, text = "delayed UI" }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); peer.Session.CancelClipboardPull();
        using (var ack = await peer.NextAsync())
            check(!ack.RootElement.GetProperty("ok").GetBoolean() && writes == 1, "clipboard cancellation reaches a queued UI writer");
        peer.Session.ClipboardReadAllowed = () => false;
        peer.Session.BeginEdgeReturn(PhoneSide.Right);peer.Session.EndEdgeReturn();peer.Session.ReleaseInputs();
        using (var capture = await peer.NextAsync()) { }
        using (var capture = await peer.NextAsync()) { }
        using (var release = await peer.NextAsync())
            check(release.RootElement.GetProperty("type").GetString() == "release", "lock/close stops capture without opening a clipboard window");
        foreach (string invalid in new[] { "", "bad\0text", new string('Я', 8001), new string('\u0001', 16000) })
        {
            bool rejected = false;
            try { peer.Session.PushClipboard(invalid); } catch (Exception e) when (e is InvalidDataException or ArgumentException) { rejected = true; }
            check(rejected && peer.Session.IsAlive, "invalid PC clipboard is rejected before queuing, without disconnect");
        }
        await using var legacy = await Peer.CreateAsync();legacy.Session.ConfigureClipboard(true);
        check(!legacy.Session.AutomaticClipboard && !legacy.Session.PushClipboard("legacy"), "no automatic command is sent to an old peer");
    }
    private static async Task<string> PullAsync(Peer peer)
    {
        peer.Session.BeginEdgeReturn(PhoneSide.Right);peer.Session.EndEdgeReturn();
        using var capture = await peer.NextAsync();using var returned = await peer.NextAsync();using var pull = await peer.NextAsync();
        if (pull.RootElement.GetProperty("type").GetString() != "clipboardGet") throw new Exception("Missing clipboard pull");
        return pull.RootElement.GetProperty("request").GetString()!;
    }
    private static async Task<JsonDocument> AutoReplyAsync(Peer peer, string request, string text)
    {
        await peer.Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id = Guid.NewGuid().ToString("N"), request, text }));
        return await peer.NextAsync();
    }
    private sealed class Peer : IAsyncDisposable
    {
        private readonly TcpClient client;
        private readonly X509Certificate2 certificate;
        public SslStream Stream { get; }
        public LanSession Session { get; }
        private Peer(TcpClient client, SslStream stream, LanSession session, X509Certificate2 certificate)
        { this.client = client; Stream = stream; Session = session; this.certificate = certificate; }
        public static async Task<Peer> CreateAsync(bool automatic = false)
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
            LanSession session = new(accepted, server, "clipboard test", automatic); session.Start();
            return new(client, stream, session, certificate);
        }
        public async Task<JsonDocument> RequestAsync(string id, string text)
        {
            await Stream.WriteAsync(LanProtocol.Encode(new { type = "clipboard", id, text }));
            return await NextAsync();
        }
        public async Task<JsonDocument> NextAsync()
        {
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
