using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace GalaxyBridge.Windows;

// Only these two dependencies are fixtures. LanHost, LanSession, framing and
// TLS are production code. Real DPAPI/key persistence is tested separately by
// GalaxyBridge.LanTls.Tests on Windows. Never include this file in the app.
internal sealed class Settings
{
    public static string Folder => Path.Combine(Path.GetTempPath(),"GalaxyBridge-HostTests");
    public bool NativeReceiveEnabled => false;
    public string NativeReceiveFolder => "";
}

internal sealed class LanIdentity : IDisposable
{
    private string phone = "", token = "";
    public X509Certificate2 Certificate { get; }
    public string HostId { get; } = Guid.NewGuid().ToString("N");
    public string Pin => Convert.ToHexString(SHA256.HashData(Certificate.RawData)).ToLowerInvariant();
    public bool HasPhone => phone.Length != 0;
    public LanIdentity()
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new("CN=Galaxy Bridge host test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        byte[] pfx = generated.Export(X509ContentType.Pfx);
        try
        {
            // Match the production LanIdentity import: Schannel requires an
            // accessible private-key container, not the generated transient key.
            Certificate = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
    public string Remember(string device) { phone = device; token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(); return token; }
    public bool Authorize(string device, string supplied) => device == phone && supplied == token && token.Length == 64;
    public void Forget() { phone = token = ""; }
    public void Dispose() => Certificate.Dispose();
}
