using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using GalaxyBridge.Core;
namespace GalaxyBridge.Windows;

internal sealed class LanHost : IAsyncDisposable
{
    private readonly LanIdentity identity = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TcpListener listener = new(IPAddress.Any, LanProtocol.Port);
    private readonly object gate = new();
    private readonly HashSet<Task> clients = [];
    private readonly SemaphoreSlim slots = new(4);
    private UdpClient? discovery;
    private Task accept = Task.CompletedTask, browse = Task.CompletedTask;
    private string? ticket;
    private long expires;
    private bool paused;
    private LanSession? current;
    public event Action<LanSession>? Connected;
    public event Action<string>? State;
    public string Pin => identity.Pin;
    public string HostId => identity.HostId;
    public bool HasPhone => identity.HasPhone;
    public void Start()
    {
        listener.Start(8); accept = AcceptAsync();
        try { discovery = new UdpClient(new IPEndPoint(IPAddress.Any, LanProtocol.DiscoveryPort)); browse = DiscoverAsync(); }
        catch (SocketException) { State?.Invoke("Обнаружение сети недоступно; QR с текущим адресом продолжает работать."); }
    }
    public string NewQr(IPAddress address)
    {
        lock (gate)
        {
            paused = false; ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            expires = Environment.TickCount64 + 120000;
            return $"galaxybridge://pair?v=1&host={address}&port={LanProtocol.Port}&id={identity.HostId}&pin={identity.Pin}&ticket={ticket}&name={Uri.EscapeDataString(Environment.MachineName)}";
        }
    }
    public bool PairingOpen { get { lock (gate) return ticket is not null && Environment.TickCount64 < expires; } }
    public void ClosePairing() { lock (gate) ticket = null; }
    public void Pause() { lock (gate) { paused = true; ticket = null; current?.Close(); } }
    public void Resume() { lock (gate) paused = false; }
    public void Forget() { lock (gate) { identity.Forget(); ticket = null; current?.Close(); } }
    private async Task AcceptAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(lifetime.Token);
                if (!slots.Wait(0)) { client.Dispose(); continue; }
                Task task = ServeAsync(client);
                lock (gate) clients.Add(task);
                _ = task.ContinueWith(completed => { lock (gate) clients.Remove(completed); }, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task ServeAsync(TcpClient client)
    {
        LanSession? session = null;
        string stage = "TLS";
        await Task.Yield();
        try
        {
            using (client)
            using (SslStream stream = new(client.GetStream(), false))
            {
                client.NoDelay = true;
                using CancellationTokenSource hello = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                hello.CancelAfter(TimeSpan.FromSeconds(8));
                State?.Invoke("Телефон открыл соединение. Проверяем TLS…");
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                { ServerCertificate = identity.Certificate, EnabledSslProtocols = SslProtocols.Tls12 }, hello.Token);
                stage = "запрос сопряжения";
                State?.Invoke("TLS установлен. Ожидаем запрос телефона…");
                using var message = await LanProtocol.ReadAsync(stream, hello.Token);
                var root = message.RootElement;
                string kind = root.GetProperty("type").GetString() ?? "";
                string phone = root.GetProperty("device").GetString() ?? "";
                if (root.GetProperty("v").GetInt32() != LanProtocol.Version || !LanProtocol.Identifier(phone)) return;
                string name = root.GetProperty("name").GetString() ?? "Phone";
                if (name.Length > 80 || name.Any(char.IsControl)) return;
                string? token = null;
                lock (gate)
                {
                    if (paused) { State?.Invoke("Соединение отклонено: подключение остановлено."); return; }
                    if (kind == "pair")
                    {
                        string supplied = root.GetProperty("ticket").GetString() ?? "";
                        if (ticket is null || Environment.TickCount64 >= expires || !LanProtocol.HexSecret(supplied) ||
                            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(supplied), Convert.FromHexString(ticket)))
                        { State?.Invoke("QR истёк или заменён. Покажите новый QR и подтвердите ноутбук на телефоне."); return; }
                        ticket = null; token = identity.Remember(phone);
                    }
                    else if (kind != "hello" || !identity.Authorize(phone, root.GetProperty("token").GetString() ?? ""))
                    { State?.Invoke("Сохранённое сопряжение не принято. Повторите сопряжение новым QR."); return; }
                    session = new LanSession(client, stream, name);
                    current?.Close(); current = session;
                }
                await stream.WriteAsync(LanProtocol.Encode(new { type = "welcome", v = LanProtocol.Version, token, host = identity.HostId }), hello.Token);
                // Explicit acknowledgement proves the phone saved its new token before any control session is accepted.
                stage = "подтверждение телефона";
                State?.Invoke("Ожидаем подтверждение сохранения сопряжения…");
                using var saved = await LanProtocol.ReadAsync(stream, hello.Token);
                if (saved.RootElement.GetProperty("type").GetString() != "saved") return;
                stage = "связь";
                session.Start(); State?.Invoke("Сопряжение подтверждено телефоном."); Connected?.Invoke(session);
                await session.Completion;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or OperationCanceledException or
            System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or CryptographicException or UnauthorizedAccessException)
        {
            if (!lifetime.IsCancellationRequested)
            {
                Exception detail = ex.GetBaseException();
                int code = detail is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : detail.HResult;
                // Only local stage/type/code: never show a QR, token, packet or file content.
                State?.Invoke($"Ошибка: {stage}; {detail.GetType().Name}, 0x{code:X8}. Повторите подключение. Если ошибка остаётся, пришлите эту строку.");
            }
        }
        finally
        {
            if (session is not null) { session.Close(); await session.Completion; lock (gate) { if (current == session) current = null; } }
            slots.Release();
        }
    }
    private async Task DiscoverAsync()
    {
        UdpClient udp = discovery!;
        long window = 0; int count = 0;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                UdpReceiveResult packet = await udp.ReceiveAsync(lifetime.Token);
                long now = Environment.TickCount64;
                if (now - window > 1000) { window = now; count = 0; }
                if (++count > 20 || packet.Buffer.Length > 128) continue;
                string[] fields = Encoding.ASCII.GetString(packet.Buffer).Split(' ');
                if (fields.Length != 3 || fields[0] != "GB_DISCOVER1" || fields[1] != identity.HostId || !LanProtocol.Identifier(fields[2])) continue;
                lock (gate) { if (paused) continue; }
                byte[] reply = Encoding.ASCII.GetBytes($"GB_HERE1 {identity.HostId} {fields[2]} {LanProtocol.Port}");
                await udp.SendAsync(reply, packet.RemoteEndPoint, lifetime.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    public async ValueTask DisposeAsync()
    {
        Pause(); lifetime.Cancel(); listener.Stop(); discovery?.Dispose();
        await Task.WhenAll(accept, browse);
        Task[] running; lock (gate) running = clients.ToArray(); await Task.WhenAll(running);
        identity.Dispose(); slots.Dispose(); lifetime.Dispose();
    }
}
