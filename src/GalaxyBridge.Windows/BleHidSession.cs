using System.Runtime.InteropServices.WindowsRuntime;
using GalaxyBridge.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace GalaxyBridge.Windows;

// Standard HOGP peripheral. Android's Bluetooth HID host owns the cursor,
// clicks, hover, selection and scrolling; the LAN service never injects these.
internal sealed class BleHidSession : IPhoneControl, IAsyncDisposable
{
    private readonly HidReportPump pump;
    private readonly object gate = new();
    private readonly List<GattServiceProvider> providers = [];
    private GattLocalCharacteristic? keyboard, mouse;
    private volatile string? selected;
    private volatile bool suspended, disposed;
    private long captureGeneration;
    private byte[] keyboardValue = new byte[8], mouseValue = new byte[Hid.MouseReportLength];
    private LanSession? captureClipboard;
    public LanSession? ClipboardSession { get; set; }
    public event Action? Changed;
    public event Action<string>? Fault;
    public bool IsAlive => !disposed && !suspended && !pump.Failed && selected is { } id && Peers().Contains(id);
    public PhoneEdgeSample? LatestEdge => null; // No fake position from LAN touch coordinates.
    private static Guid Uuid(ushort value) => new($"0000{value:x4}-0000-1000-8000-00805f9b34fb");
    public BleHidSession()
    {
        pump = new(NotifyAsync);
        pump.Fault += message => Fault?.Invoke(message);
    }
    public async Task StartAsync(CancellationToken ct)
    {
        BluetoothAdapter? adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(ct);
        if (adapter is null) throw new IOException("Адаптер Bluetooth не найден. Включите Bluetooth на ноутбуке.");
        if (!adapter.IsPeripheralRoleSupported) throw new IOException("Адаптер ноутбука не поддерживает Bluetooth LE Peripheral. Системная мышь без отладки на этом адаптере недоступна; используйте Wi-Fi + QR в режиме ADB.");
        GattServiceProvider hid = await ProviderAsync(0x1812, ct);
        await ConstantAsync(hid, 0x2A4A, [0x11, 0x01, 0, 2], ct);
        await ConstantAsync(hid, 0x2A4B, Hid.BluetoothReportMap, ct);
        keyboard = await ReportAsync(hid, 1, true, ct);
        mouse = await ReportAsync(hid, 2, true, ct);
        GattLocalCharacteristic leds = await ReportAsync(hid, 1, false, ct);
        leds.WriteRequested += LedWritten;
        GattLocalCharacteristic control = await CharacteristicAsync(hid, 0x2A4C, new()
        {
            CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired
        }, ct);
        control.WriteRequested += ControlWritten;
        // Battery Service is standard HOGP metadata. This virtual peripheral is
        // powered by the PC, so it reports 100%, not the phone's battery state.
        GattServiceProvider battery = await ProviderAsync(0x180F, ct);
        await ConstantAsync(battery, 0x2A19, [100], ct);
        foreach (GattServiceProvider provider in providers)
        {
            provider.AdvertisementStatusChanged += (_, args) =>
            {
                if (args.Status == GattServiceProviderAdvertisementStatus.Aborted)
                { suspended = true;pump.Stop();Fault?.Invoke("Bluetooth прекратил объявление устройства: " + args.Error); }
                Changed?.Invoke();
            };
            provider.StartAdvertising(new() { IsConnectable = true, IsDiscoverable = true });
        }
        Changed?.Invoke();
    }
    private async Task<GattServiceProvider> ProviderAsync(ushort uuid, CancellationToken ct)
    {
        GattServiceProviderResult result = await GattServiceProvider.CreateAsync(Uuid(uuid)).AsTask(ct);
        if (result.Error != BluetoothError.Success) throw new IOException("Не удалось создать Bluetooth HID: " + result.Error);
        providers.Add(result.ServiceProvider);return result.ServiceProvider;
    }
    private static async Task<GattLocalCharacteristic> CharacteristicAsync(GattServiceProvider service, ushort uuid, GattLocalCharacteristicParameters parameters, CancellationToken ct)
    {
        GattLocalCharacteristicResult result = await service.Service.CreateCharacteristicAsync(Uuid(uuid), parameters).AsTask(ct);
        if (result.Error != BluetoothError.Success) throw new IOException("Не удалось создать характеристику Bluetooth: " + result.Error);
        return result.Characteristic;
    }
    private static Task<GattLocalCharacteristic> ConstantAsync(GattServiceProvider service, ushort uuid, byte[] value, CancellationToken ct) =>
        CharacteristicAsync(service, uuid, new()
        {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = value.AsBuffer()
        }, ct);
    private async Task<GattLocalCharacteristic> ReportAsync(GattServiceProvider service, byte report, bool input, CancellationToken ct)
    {
        GattLocalCharacteristic characteristic = await CharacteristicAsync(service, 0x2A4D, new()
        {
            CharacteristicProperties = input ? GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify :
                GattCharacteristicProperties.Read | GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            WriteProtectionLevel = GattProtectionLevel.EncryptionRequired
        }, ct);
        GattLocalDescriptorResult reference = await characteristic.CreateDescriptorAsync(Uuid(0x2908), new()
        {
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            StaticValue = new byte[] { report, input ? (byte)1 : (byte)2 }.AsBuffer()
        }).AsTask(ct);
        if (reference.Error != BluetoothError.Success) throw new IOException("Не удалось описать Bluetooth HID-отчёт: " + reference.Error);
        characteristic.ReadRequested += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                GattReadRequest? request = await args.GetRequestAsync();
                if (request is null) return;
                byte[] value;
                lock (gate)
                {
                    bool chosen = args.Session.DeviceId.Id == selected;
                    value = !input ? new byte[1] : report == 1 ? chosen ? keyboardValue : new byte[8] : new byte[Hid.MouseReportLength];
                }
                // Reading mouse state must never replay relative movement/wheel.
                if (input && report == 2) { lock (gate) if (args.Session.DeviceId.Id == selected) value[0] = mouseValue[0]; }
                if (request.Offset > value.Length) request.RespondWithProtocolError(0x07);
                else request.RespondWithValue(value.AsBuffer((int)request.Offset, value.Length - (int)request.Offset));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException) { }
            finally { deferral.Complete(); }
        };
        if (input) characteristic.SubscribedClientsChanged += (_, _) => Changed?.Invoke();
        return characteristic;
    }
    private async void LedWritten(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            GattWriteRequest? request = await args.GetRequestAsync();
            if (request is null) return;
            if (request.Offset != 0 || request.Value.Length != 1) request.RespondWithProtocolError(0x0D);
            else if (request.Option == GattWriteOption.WriteWithResponse) request.Respond();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException) { }
        finally { deferral.Complete(); }
    }
    private async void ControlWritten(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            GattWriteRequest? request = await args.GetRequestAsync();
            if (request is null) return;
            if (request.Offset != 0 || request.Value.Length != 1) { request.RespondWithProtocolError(0x0D);return; }
            using DataReader reader = DataReader.FromBuffer(request.Value);
            byte value = reader.ReadByte();
            if (value > 1) { request.RespondWithProtocolError(0x13);return; }
            if (args.Session.DeviceId.Id == selected)
            { suspended = value == 0;if (suspended) pump.Stop();Changed?.Invoke(); }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException) { }
        finally { deferral.Complete(); }
    }
    public string[] Peers()
    {
        if (disposed || keyboard is null || mouse is null) return [];
        try
        {
            string[] keys = keyboard.SubscribedClients.Where(c => c.Session.SessionStatus == GattSessionStatus.Active).Select(c => c.Session.DeviceId.Id).ToArray();
            return mouse.SubscribedClients.Where(c => c.Session.SessionStatus == GattSessionStatus.Active).Select(c => c.Session.DeviceId.Id).Intersect(keys).ToArray();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return []; }
    }
    public void Select(string id)
    {
        if (pump.IsActive) throw new InvalidOperationException("Сначала верните управление на ПК.");
        selected = id;suspended = false;
        lock (gate) { keyboardValue = new byte[8];mouseValue = new byte[Hid.MouseReportLength]; }
    }
    public async Task<string> PeerNameAsync(string id, CancellationToken ct)
    {
        using BluetoothLEDevice? peer = await BluetoothLEDevice.FromIdAsync(id).AsTask(ct);
        return string.IsNullOrWhiteSpace(peer?.Name) ? "Телефон Bluetooth" : peer.Name;
    }
    private async Task NotifyAsync(string id, byte report, byte[] payload, CancellationToken ct)
    {
        GattLocalCharacteristic characteristic = (report == 1 ? keyboard : mouse) ?? throw new IOException("Bluetooth HID не запущен.");
        GattSubscribedClient? client = characteristic.SubscribedClients.FirstOrDefault(c => c.Session.DeviceId.Id == id && c.Session.SessionStatus == GattSessionStatus.Active);
        if (client is null)
        {
            if (payload.All(b => b == 0)) return; // Disconnected host has no keys to release.
            throw new IOException("Подписка телефона на Bluetooth-ввод потеряна.");
        }
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        GattClientNotificationResult result = await characteristic.NotifyValueAsync(payload.AsBuffer(), client).AsTask(deadline.Token);
        if (result.Status != GattCommunicationStatus.Success) throw new IOException("Bluetooth не подтвердил передачу ввода: " + result.Status);
        lock (gate)
        {
            if (id == selected) { if (report == 1) keyboardValue = (byte[])payload.Clone();else mouseValue = (byte[])payload.Clone(); }
        }
    }
    public int BeginEdgeReturn(PhoneSide side)
    {
        if (!IsAlive || selected is null) throw new InvalidOperationException("Сначала подключите телефон в настройках Bluetooth.");
        captureGeneration++;captureClipboard = ClipboardSession;captureClipboard?.BeginNativeControl();
        pump.Begin(selected);return 0;
    }
    public void EndEdgeReturn()
    {
        long generation = ++captureGeneration;LanSession? source = captureClipboard;
        pump.Stop();_ = ReadClipboardAfterReleaseAsync(generation, source);
    }
    private async Task ReadClipboardAfterReleaseAsync(long generation, LanSession? source)
    {
        try
        {
            // One in-flight report plus two neutral notifications can each use
            // their two-second deadline. The clipboard read follows all three.
            await pump.FlushAsync().WaitAsync(TimeSpan.FromSeconds(7));
        }
        catch (TimeoutException) { }
        if (generation == captureGeneration && !pump.IsActive) source?.EndNativeControl();
    }
    public bool Send(byte[] packet) => false; // Clipboard text goes through the LAN listener; Ctrl+V stays a real HID shortcut.
    public void Keyboard(byte[] report) => pump.Post(1, report);
    public void Mouse(byte buttons, int dx, int dy, int wheel = 0, int horizontalWheel = 0)
    { foreach (byte[] report in Hid.MouseReports(buttons, dx, dy, wheel, horizontalWheel)) if (!pump.Post(2, report)) break; }
    public void ReleaseInputs() => pump.Stop();
    public async ValueTask DisposeAsync()
    {
        await pump.DisposeAsync();disposed = true;captureGeneration++;
        foreach (GattServiceProvider provider in providers)
        { try { provider.StopAdvertising(); } catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { } }
        providers.Clear();keyboard = mouse = null;
    }
}
