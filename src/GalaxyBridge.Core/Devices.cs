using System.Net;

namespace GalaxyBridge.Core;

public sealed record AdbDevice(string Serial, string State, string Model)
{
    public bool Ready => State == "device";
    public override string ToString() => $"{Model} — {State} [{Serial}]";
    public static IReadOnlyList<AdbDevice> Parse(string output)
    {
        List<AdbDevice> devices = [];
        foreach (string line in output.Split('\n'))
        {
            string[] parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] == "List" || parts[0] == "*" ||
                parts[1] is not ("device" or "offline" or "unauthorized" or "no")) continue;
            string model = parts.FirstOrDefault(p => p.StartsWith("model:", StringComparison.Ordinal))?[6..].Replace('_', ' ') ?? "Android";
            devices.Add(new AdbDevice(parts[0], parts[1] == "no" ? "no permissions" : parts[1], model));
        }
        return devices;
    }
}

public readonly record struct AdbEndpoint(string Address, int Port)
{
    public static AdbEndpoint Parse(string value)
    {
        value = value.Trim();
        int colon = value.LastIndexOf(':');
        if (colon < 1 || !int.TryParse(value[(colon + 1)..], out int port) || port is < 1 or > 65535)
            throw new FormatException("Введите IP-адрес и порт из настроек телефона, например 192.168.1.10:37121.");
        string host = value[..colon].Trim('[', ']');
        if (!IPAddress.TryParse(host, out IPAddress? address))
            throw new FormatException("Нужен IP-адрес телефона, а не адрес сайта.");
        return new AdbEndpoint(address.ToString(), port);
    }
    public override string ToString() => Address.Contains(':') ? $"[{Address}]:{Port}" : $"{Address}:{Port}";
}

public enum PhoneSide { Right, Left, Top, Bottom }
public readonly record struct DesktopBounds(int X, int Y, int Width, int Height);
public static class EdgePolicy
{
    public static bool AtEdge(DesktopBounds b, int x, int y, PhoneSide side) => side switch
    {
        PhoneSide.Right => x == b.X + b.Width - 1 && y >= b.Y && y < b.Y + b.Height,
        PhoneSide.Left => x == b.X && y >= b.Y && y < b.Y + b.Height,
        PhoneSide.Top => y == b.Y && x >= b.X && x < b.X + b.Width,
        PhoneSide.Bottom => y == b.Y + b.Height - 1 && x >= b.X && x < b.X + b.Width,
        _ => false
    };
}
