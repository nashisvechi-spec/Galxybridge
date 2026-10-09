using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using GalaxyBridge.Core;
namespace GalaxyBridge.Windows;

internal sealed class LanIdentity : IDisposable
{
    private sealed record Stored(string HostId, string Pfx, string PhoneId, string Token);
    private static string FileName => Path.Combine(Settings.Folder, "lan-identity.dat");
    private Stored state;
    public X509Certificate2 Certificate { get; }
    public string HostId => state.HostId;
    public string Pin => Convert.ToHexString(SHA256.HashData(Certificate.RawData)).ToLowerInvariant();
    public bool HasPhone => LanProtocol.Identifier(state.PhoneId) && LanProtocol.HexSecret(state.Token);
    public LanIdentity()
    {
        if (File.Exists(FileName))
        {
            // Never silently regenerate identity: the phone's pinned certificate would stop matching.
            byte[] json = ProtectedData.Unprotect(File.ReadAllBytes(FileName), null, DataProtectionScope.CurrentUser);
            try { state = JsonSerializer.Deserialize<Stored>(json) ?? throw new InvalidDataException("Invalid LAN identity."); }
            finally { CryptographicOperations.ZeroMemory(json); }
            if (!LanProtocol.Identifier(state.HostId)) throw new InvalidDataException("Invalid LAN identity.");
            byte[] pfx = Convert.FromBase64String(state.Pfx);
            try { Certificate = ImportCertificate(pfx); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
        else
        {
            using RSA key = RSA.Create(2048);
            CertificateRequest request = new("CN=Galaxy Bridge local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
            byte[] pfx = generated.Export(X509ContentType.Pfx);
            try
            {
                Certificate = ImportCertificate(pfx);
                state = new(Guid.NewGuid().ToString("N"), Convert.ToBase64String(pfx), "", "");
            }
            finally { CryptographicOperations.ZeroMemory(pfx); }
            try { Save(); } catch { Certificate.Dispose(); throw; }
        }
    }
    // Schannel (Windows TLS) cannot use an EphemeralKeySet private key.
    // Import in the current user's key container; without PersistKeySet the
    // temporary key is deleted when this certificate is disposed. Do not add
    // the certificate to a trusted/root store or change its existing pin.
    internal static X509Certificate2 ImportCertificate(byte[] pfx) =>
        new(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
    public bool Authorize(string phone, string token) => phone == state.PhoneId && LanProtocol.HexSecret(token) &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(token), Convert.FromHexString(state.Token));
    public string Remember(string phone)
    {
        if (!LanProtocol.Identifier(phone)) throw new InvalidDataException("Invalid phone identity.");
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Stored previous = state; state = state with { PhoneId = phone, Token = token };
        try { Save(); } catch { state = previous; throw; }
        return token;
    }
    public void Forget() { Stored previous = state; state = state with { PhoneId = "", Token = "" }; try { Save(); } catch { state = previous; throw; } }
    private void Save()
    {
        Directory.CreateDirectory(Settings.Folder);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(state);
        try
        {
            byte[] protectedData = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FileName + ".tmp", protectedData); File.Move(FileName + ".tmp", FileName, true);
        }
        finally { CryptographicOperations.ZeroMemory(json); }
    }
    public void Dispose() => Certificate.Dispose();
}
