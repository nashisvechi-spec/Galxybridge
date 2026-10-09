using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

/// <summary>Direct local queries when the running ADB server's mDNS cache is unavailable.</summary>
internal static class LocalMdnsDiscovery
{
    public static Task<IReadOnlyList<AdbMdnsService>> PairingAsync(AdbQrPairing qr, CancellationToken ct) =>
        FindAsync("_adb-tls-pairing._tcp", qr.ServiceName, null, ct);
    public static Task<IReadOnlyList<AdbMdnsService>> ConnectAsync(string address, CancellationToken ct) =>
        FindAsync("_adb-tls-connect._tcp", null, address, ct);

    private static async Task<IReadOnlyList<AdbMdnsService>> FindAsync(string type, string? instance, string? address, CancellationToken ct)
    {
        IPAddress[] interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 1 : 2)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => a.Address))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.Equals(IPAddress.Any))
            .Distinct().Take(8).ToArray();
        IReadOnlyList<AdbMdnsService>[] results = await Task.WhenAll(interfaces.Select(a => QueryAsync(a, type, instance, address, ct)));
        return results.SelectMany(s => s).Distinct().ToArray();
    }

    private static async Task<IReadOnlyList<AdbMdnsService>> QueryAsync(IPAddress local, string type,
        string? instance, string? address, CancellationToken ct)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(1400);
        HashSet<(MdnsRecord Record, string Source)> records = [];
        HashSet<(string Name, ushort Type)> queries = [];
        string browse = type + ".local";
        ushort id = (ushort)Random.Shared.Next(1, 65536);
        try
        {
            using UdpClient udp = new(AddressFamily.InterNetwork);
            udp.Client.Bind(new IPEndPoint(local, 0));
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            IPEndPoint group = new(IPAddress.Parse("224.0.0.251"), 5353);
            async Task SendAsync(string name, ushort recordType)
            {
                if (queries.Count >= 24 || !queries.Add((name, recordType))) return;
                byte[] packet;
                try { packet = MdnsPacket.Query(name, recordType, id); }
                catch (FormatException) { return; }
                _ = await udp.SendAsync(packet.AsMemory(), group, deadline.Token);
            }
            await SendAsync(browse, 12);
            if (instance is not null) await SendAsync(instance + "." + browse, 33);
            for (int received = 0; received < 64; received++)
            {
                UdpReceiveResult response = await udp.ReceiveAsync(deadline.Token);
                if (response.RemoteEndPoint.Port != 5353) continue;
                IReadOnlyList<MdnsRecord> parsed;
                try { parsed = MdnsPacket.Parse(response.Buffer, id); }
                catch (FormatException) { continue; }
                string source = response.RemoteEndPoint.Address.ToString();
                foreach (MdnsRecord record in parsed)
                {
                    if (records.Count < 256) records.Add((record, source));
                    if (record.Type == 12 && record.Name.Equals(browse, StringComparison.OrdinalIgnoreCase) &&
                        record.Target.EndsWith("." + browse, StringComparison.OrdinalIgnoreCase) &&
                        (instance is null || record.Target.Equals(instance + "." + browse, StringComparison.OrdinalIgnoreCase)))
                        await SendAsync(record.Target, 33);
                    if (record.Type == 33 && record.Name.EndsWith("." + browse, StringComparison.OrdinalIgnoreCase) &&
                        record.Target.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
                        (instance is null || record.Name.Equals(instance + "." + browse, StringComparison.OrdinalIgnoreCase)))
                        await SendAsync(record.Target, 1);
                }
                IReadOnlyList<AdbMdnsService> found = Services();
                if (found.Count > 0) return found;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
        ct.ThrowIfCancellationRequested();
        return Services();

        IReadOnlyList<AdbMdnsService> Services()
        {
            List<AdbMdnsService> found = [];
            foreach ((MdnsRecord srv, string source) in records.Where(r => r.Record.Type == 33))
            {
                if (!srv.Name.EndsWith("." + browse, StringComparison.OrdinalIgnoreCase)) continue;
                string name = srv.Name[..^(browse.Length + 1)];
                if (name.Length is < 1 or > 128 || name.Contains('.') ||
                    (instance is not null && !name.Equals(instance, StringComparison.OrdinalIgnoreCase))) continue;
                // Only accept the responder's own IPv4 address, never an advertised external host.
                if (address is not null && source != address) continue;
                if (records.Any(r => r.Record.Type == 1 && r.Source == source && r.Record.Address == source &&
                    r.Record.Name.Equals(srv.Target, StringComparison.OrdinalIgnoreCase)))
                    found.Add(new(instance ?? name, type, new(source, srv.Port)));
            }
            return found.Distinct().ToArray();
        }
    }
}
