using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GalaxyBridge.Windows;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}
string Pin(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));

async Task Handshake(X509Certificate2 certificate, string expectedPin, bool reject = false)
{
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
    using TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    async Task Server()
    {
        using TcpClient peer = await listener.AcceptTcpClientAsync(timeout.Token);
        using SslStream tls = new(peer.GetStream(), false);
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 }, timeout.Token);
        if (!reject)
        {
            byte[] request = new byte[1]; await tls.ReadExactlyAsync(request, timeout.Token);
            Check(request[0] == 42, "Client payload not received through TLS");
            await tls.WriteAsync(new byte[] { 43 }, timeout.Token);
        }
    }
    Task server = Server();
    try
    {
        using TcpClient peer = new(); await peer.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        using SslStream tls = new(peer.GetStream(), false, (_, remote, _, _) =>
            remote is not null && Convert.ToHexString(SHA256.HashData(remote.GetRawCertData())) == expectedPin);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        { TargetHost = "Galaxy Bridge local", EnabledSslProtocols = SslProtocols.Tls12 }, timeout.Token);
        if (reject) throw new InvalidOperationException("A wrong certificate pin was accepted");
        Check(tls.IsEncrypted && tls.SslProtocol == SslProtocols.Tls12, "TLS 1.2 not negotiated");
        await tls.WriteAsync(new byte[] { 42 }, timeout.Token);
        byte[] response = new byte[1]; await tls.ReadExactlyAsync(response, timeout.Token);
        Check(response[0] == 43, "Server payload not received through TLS");
    }
    catch (AuthenticationException) when (reject) { Check(true, "Wrong pin rejected"); }
    finally
    {
        try { await server; }
        catch (Exception ex) when (reject && ex is AuthenticationException or IOException) { }
    }
}

// Exercise the exact import used by the application, including a re-import
// of the same PFX. On the Windows workflow this executes Schannel, not OpenSSL.
using RSA key = RSA.Create(2048);
CertificateRequest request = new("CN=Galaxy Bridge local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
byte[] pfx = generated.Export(X509ContentType.Pfx);
try
{
    string pin = Pin(generated);
    using (X509Certificate2 imported = LanIdentity.ImportCertificate(pfx))
    {
        Check(imported.HasPrivateKey && Pin(imported) == pin, "Import changed identity");
        await Handshake(imported, pin);
        await Handshake(imported, new string('0', 64), reject: true);
    }
    using (X509Certificate2 reloaded = LanIdentity.ImportCertificate(pfx)) await Handshake(reloaded, pin);
}
finally { CryptographicOperations.ZeroMemory(pfx); }

// Validate production DPAPI storage/reload as well, without touching app data.
if (OperatingSystem.IsWindows())
{
    try
    {
        string pin, hostId, token; string device = Guid.NewGuid().ToString("N");
        using (LanIdentity first = new())
        {
            pin = first.Pin; hostId = first.HostId; token = first.Remember(device);
            await Handshake(first.Certificate, pin.ToUpperInvariant());
        }
        using (LanIdentity reloaded = new())
        {
            Check(reloaded.Pin == pin && reloaded.HostId == hostId, "Stored certificate pin/host changed");
            Check(reloaded.Authorize(device, token), "Stored pairing did not survive reload");
            await Handshake(reloaded.Certificate, pin.ToUpperInvariant());
        }
    }
    finally { if (Directory.Exists(Settings.Folder)) Directory.Delete(Settings.Folder, true); }
}
else Console.WriteLine("SKIP: Windows DPAPI and Schannel checks require Windows; local TLS uses OpenSSL.");
Console.WriteLine($"PASS: {checks} LAN TLS assertions");
