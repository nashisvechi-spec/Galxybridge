using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed record DiscoveredPhone(AdbDevice Device, PhoneIdentity Identity, string WifiEndpoint);

internal sealed class DeviceDiscovery(AdbClient adb)
{
    public async Task<DiscoveredPhone?> FindRememberedAsync(RememberedPhone phone, CancellationToken ct)
    {
        if (!phone.Valid) return null;
        IReadOnlyList<AdbDevice> devices = await adb.DevicesAsync(ct);
        DiscoveredPhone? found = await MatchAsync(devices, phone, ct);
        if (found is not null) return found;

        IReadOnlyList<AdbMdnsService> services = [];
        try { services = await adb.MdnsAsync(ct); }
        catch (Exception ex) when (ex is IOException or TimeoutException) { }
        IEnumerable<string> endpoints = services.Where(s => s.Matches(phone.Identity)).Select(s => s.Endpoint.ToString());
        if (phone.WifiEndpoint.Length > 0) endpoints = endpoints.Append(phone.WifiEndpoint);
        foreach (string endpoint in endpoints.Distinct().Take(3))
        {
            ct.ThrowIfCancellationRequested();
            try { await adb.ConnectWifiAsync(endpoint, ct, timeoutSeconds: 3); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { continue; }
            devices = await adb.DevicesAsync(ct);
            found = await MatchAsync(devices, phone, ct);
            if (found is not null) return found with { WifiEndpoint = endpoint };
        }
        return null;
    }

    private async Task<DiscoveredPhone?> MatchAsync(IReadOnlyList<AdbDevice> devices, RememberedPhone phone, CancellationToken ct)
    {
        foreach (AdbDevice device in devices.Where(d => d.Ready).OrderByDescending(d => d.Serial == phone.TransportSerial))
        {
            ct.ThrowIfCancellationRequested();
            PhoneIdentity identity;
            try { identity = await adb.IdentityAsync(device.Serial, ct); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { continue; }
            if (!phone.Identity.Matches(identity)) continue;
            string endpoint = "";
            try { endpoint = AdbEndpoint.Parse(device.Serial).ToString(); } catch (FormatException) { }
            return new(device, identity, endpoint);
        }
        return null;
    }

    public async Task<AdbDevice?> FindEndpointAsync(AdbEndpoint endpoint, CancellationToken ct)
    {
        IReadOnlyList<AdbDevice> devices = await adb.DevicesAsync(ct);
        AdbDevice? exact = devices.FirstOrDefault(d => d.Ready && d.Serial == endpoint.ToString());
        if (exact is not null) return exact;
        IReadOnlyList<AdbMdnsService> services = await adb.MdnsAsync(ct);
        return devices.FirstOrDefault(d => d.Ready && services.Any(s => s.Endpoint == endpoint && s.OwnsTransport(d.Serial)));
    }

    public async Task<(AdbDevice Device, string Endpoint)?> WaitPairedAsync(AdbEndpoint pairing, CancellationToken ct)
    {
        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            while (true)
            {
                IReadOnlyList<AdbMdnsService> services = [];
                try { services = await adb.MdnsAsync(wait.Token); }
                catch (Exception ex) when (ex is IOException or TimeoutException) { }
                if (!services.Any(s => s.Connect && s.Endpoint.Address == pairing.Address))
                {
                    try { services = await LocalMdnsDiscovery.ConnectAsync(pairing.Address, wait.Token); }
                    catch (System.Net.NetworkInformation.NetworkInformationException) { }
                }
                // Pairing is bound to this phone's IP and this dialog's secret, never to an arbitrary ready device.
                foreach (AdbMdnsService service in services.Where(s => s.Connect && s.Endpoint.Address == pairing.Address))
                {
                    try
                    {
                        await adb.ConnectWifiAsync(service.Endpoint.ToString(), wait.Token, timeoutSeconds: 3);
                        IReadOnlyList<AdbDevice> devices = await adb.DevicesAsync(wait.Token);
                        AdbDevice? device = devices.FirstOrDefault(d => d.Ready && service.OwnsTransport(d.Serial));
                        if (device is not null) return (device, service.Endpoint.ToString());
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException) { }
                }
                await Task.Delay(1000, wait.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }
}
