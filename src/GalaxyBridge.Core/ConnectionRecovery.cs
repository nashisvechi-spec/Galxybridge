namespace GalaxyBridge.Core;

public sealed record PhoneIdentity(string HardwareSerial, string WifiGuid)
{
    public bool Known => HardwareSerial is { Length: > 0 } || WifiGuid is { Length: > 0 };
    public bool Matches(PhoneIdentity other) => HardwareSerial.Length > 0 && other.HardwareSerial.Length > 0
        ? HardwareSerial == other.HardwareSerial
        : WifiGuid.Length > 0 && WifiGuid == other.WifiGuid;

    public static PhoneIdentity ParseProperties(string output)
    {
        string serial = "", bootSerial = "", guid = "";
        foreach (string line in output.Split('\n'))
        {
            int end = line.IndexOf("]: [", StringComparison.Ordinal);
            if (!line.StartsWith('[') || end < 2 || !line.TrimEnd().EndsWith(']')) continue;
            string key = line[1..end], value = line[(end + 4)..].TrimEnd().TrimEnd(']');
            value = Clean(value);
            if (key == "ro.serialno") serial = value;
            else if (key == "ro.boot.serialno") bootSerial = value;
            else if (key == "persist.adb.wifi.guid") guid = value;
        }
        return new(serial.Length > 0 ? serial : bootSerial, guid);
    }
    public static string Clean(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
        !value.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? value : "";
}

public sealed record RememberedPhone(PhoneIdentity Identity, string Model, string TransportSerial, string WifiEndpoint)
{
    public bool Valid => Identity is not null && Identity.Known &&
        Identity.HardwareSerial == PhoneIdentity.Clean(Identity.HardwareSerial) &&
        Identity.WifiGuid == PhoneIdentity.Clean(Identity.WifiGuid) &&
        Model is { Length: <= 128 } && TransportSerial is { Length: > 0 and <= 256 } && WifiEndpoint is { Length: <= 128 };
}

public sealed record AdbMdnsService(string Instance, string Type, AdbEndpoint Endpoint)
{
    public bool Pairing => Type == "_adb-tls-pairing._tcp";
    public bool Connect => Type == "_adb-tls-connect._tcp";
    public string TransportSerial => Instance + "." + Type;
    public bool OwnsTransport(string serial) => Connect &&
        (serial == Endpoint.ToString() || serial.TrimEnd('.') == TransportSerial);
    public bool Matches(PhoneIdentity identity)
    {
        if (!Connect) return false;
        // AOSP's persistent GUID already includes adb- and the hardware serial.
        string guidName = identity.WifiGuid.StartsWith("adb-", StringComparison.Ordinal)
            ? identity.WifiGuid : "adb-" + identity.WifiGuid;
        return (identity.WifiGuid.Length > 0 && (Instance == guidName || Instance.StartsWith(guidName + "-", StringComparison.Ordinal))) ||
            (identity.HardwareSerial.Length > 0 && Instance.StartsWith("adb-" + identity.HardwareSerial + "-", StringComparison.Ordinal));
    }

    public static IReadOnlyList<AdbMdnsService> Parse(string output)
    {
        List<AdbMdnsService> result = [];
        foreach (string line in output.Split('\n'))
        {
            string[] p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 3 || p[0].Length > 128) continue;
            string type = p[1].TrimEnd('.');
            if (type is not ("_adb-tls-pairing._tcp" or "_adb-tls-connect._tcp")) continue;
            try { result.Add(new(p[0], type, AdbEndpoint.Parse(p[2]))); }
            catch (FormatException) { }
        }
        return result;
    }
}

public sealed record AdbQrPairing(string ServiceName, string Secret)
{
    public string Payload
    {
        get
        {
            if (!ServiceName.StartsWith("studio-", StringComparison.Ordinal) || ServiceName.Length != 17 ||
                !ServiceName[7..].All(char.IsAsciiLetterOrDigit) || Secret.Length != 32 || !Secret.All(char.IsAsciiHexDigit))
                throw new ArgumentException("Invalid QR pairing credentials.");
            return $"WIFI:T:ADB;S:{ServiceName};P:{Secret};;";
        }
    }
    public bool Matches(AdbMdnsService service) => service.Pairing && service.Instance == ServiceName;
    public override string ToString() => "ADB QR pairing (secret omitted)";
}

/// <summary>Monotonic retry schedule shared by startup discovery and connection recovery.</summary>
public sealed class ConnectionRecovery
{
    public bool Paused { get; private set; }
    public bool Recovering { get; private set; }
    public int Failures { get; private set; }
    public long NextAttemptAt { get; private set; }
    public bool Due(bool autoConnect, bool reconnect, bool suspended, bool attached, bool busy, long now) =>
        !Paused && !suspended && !attached && !busy && (Recovering ? reconnect : autoConnect) && now >= NextAttemptAt;
    public void Restart() { Paused = Recovering = false; Failures = 0; NextAttemptAt = 0; }
    public void Pause() => Paused = true;
    public void Lost(long now) { Recovering = true; Failures = 0; NextAttemptAt = now + 1000; }
    public void Failed(long now)
    { Failures = Math.Min(Failures + 1, 5); NextAttemptAt = now + Math.Min(30000, 1000 * (1 << Failures)); }
    public void Succeeded() => Restart();
    public void Wake(long now) => NextAttemptAt = now;
    public void Resume(long now) { Paused = false; Failures = 0; Wake(now); }
}
