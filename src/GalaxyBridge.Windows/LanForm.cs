using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using GalaxyBridge.Core;
using Microsoft.Win32;
using QRCoder;
namespace GalaxyBridge.Windows;

internal sealed class LanForm : Form
{
    private readonly TaskCompletionSource shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Shutdown => shutdown.Task;
    public bool SystemMouseQrRequested { get; private set; }
    private readonly Settings settings;
    private readonly LanHost host;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer clipboardRetry = new() { Interval = 50 };
    private uint ownClipboardSequence, lastPushedSequence;
    private int clipboardAttempts;
    private readonly ComboBox addresses = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly PictureBox qr = new() { Size = new Size(340, 340), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Visible = false };
    private readonly Label state = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly Label connectionDetail = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly Button toggle = new() { Enabled = false, Text = "Управлять телефоном", AutoSize = true }, files = new() { Enabled = false, Text = "Отправить файлы…", AutoSize = true };
    private readonly CheckBox edgeEntry = new() { Text = "Вход через край экрана", AutoSize = true };
    private readonly Label transferState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly Label receiveState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly CheckBox share = new() { Text = "Общий текстовый буфер", AutoSize = true };
    private readonly Label clipboardState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly Label bluetoothState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly ComboBox bluetoothPeers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, Visible = false };
    private readonly Button bluetooth = new() { Text = "Системная мышь по Bluetooth (тест)", AutoSize = true };
    private readonly Button bluetoothStop = new() { Text = "Вернуться к сенсорному управлению", AutoSize = true, Visible = false };
    private BleHidSession? nativeMouse;
    private Task? bluetoothOperation;
    private bool refreshingBluetooth, refreshBluetoothAgain;
    private sealed record BluetoothPeer(string Id, string Name) { public override string ToString() => Name; }
    private LanReceiveState received = new(false, 0, 0, "Файлы с телефона ещё не получены.");
    private readonly NumericUpDown speed = new() { Minimum = .25M, Maximum = 4M, Increment = .05M, DecimalPlaces = 2, Width = 90 };
    private FileDropForm? fileWindow;
    private InputCapture? capture;
    private LanSession? session;
    private CancellationTokenSource? transfer;
    private Task? sending;
    private bool closing, finished, paused, suspended, armed;
    private long? edgeSince;
    public LanForm(Settings settings)
    {
        Icon = AppIcon.Value;
        this.settings = settings; host = new(settings);
        Text = "Galaxy Bridge • Wi-Fi без отладки • 0.7.4"; Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent; Size = new Size(760, Math.Min(800, (Screen.PrimaryScreen?.WorkingArea.Height ?? 850) - 40)); MinimumSize = new Size(600, 450);
        FlowLayoutPanel page = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        page.Controls.Add(new Label { Text = "Установите GalaxyBridgeLan.apk на S25. В приложении нажмите «Сканировать QR» и подтвердите ноутбук. Отладка не нужна. Оба устройства должны быть в одной локальной сети.", AutoSize = true, MaximumSize = new Size(660, 0) });
        page.Controls.Add(state);
        page.Controls.Add(connectionDetail);
        share.Checked = settings.ClipboardEnabled; page.Controls.Add(share);
        clipboardState.Text = "Копируйте на ПК и вставляйте на телефоне. Скопировав на телефоне, верните курсор на ПК и вставьте через Ctrl+V. На телефоне разрешите «Поверх других приложений».";
        page.Controls.Add(clipboardState);
        share.CheckedChanged += (_, _) =>
        {
            settings.ClipboardEnabled = share.Checked; session?.ConfigureClipboard(share.Checked);
            clipboardRetry.Stop(); lastPushedSequence = Native.GetClipboardSequenceNumber();
            try { settings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { clipboardState.Text = "Настройка буфера применена, но не сохранена на диск."; }
        };
        Button renew = new() { Text = "Показать новый QR (2 минуты)", AutoSize = true };
        FlowLayoutPanel pair = new() { AutoSize = true }; pair.Controls.Add(addresses); pair.Controls.Add(renew); page.Controls.Add(pair); page.Controls.Add(qr);
        side.Items.AddRange(["Справа", "Слева", "Сверху", "Снизу"]); side.SelectedIndex = (int)settings.PhoneSide;
        edgeEntry.Checked = settings.EdgeEntryEnabled;
        FlowLayoutPanel control = new() { AutoSize = true }; control.Controls.Add(side); control.Controls.Add(toggle); control.Controls.Add(edgeEntry); page.Controls.Add(control);
        FlowLayoutPanel nativeActions = new() { AutoSize = true };nativeActions.Controls.Add(bluetooth);nativeActions.Controls.Add(bluetoothStop);page.Controls.Add(nativeActions);
        page.Controls.Add(bluetoothPeers);page.Controls.Add(bluetoothState);
        bluetoothState.Text = "Сенсорный режим: касания и свайпы. Системный режим: обычная Bluetooth-мышь и клавиатура; отладка не нужна. Требуется поддержка Bluetooth LE Peripheral на ноутбуке.";
        bluetooth.Click += async (_, _) =>
        {
            if (bluetoothOperation is not null) return;
            bluetoothOperation = StartBluetoothAsync();
            try { await bluetoothOperation; } finally { bluetoothOperation = null; }
        };
        bluetoothStop.Click += async (_, _) =>
        {
            if (bluetoothOperation is not null) return;
            bluetoothOperation = StopBluetoothAsync();
            try { await bluetoothOperation; } finally { bluetoothOperation = null; }
        };
        bluetoothPeers.SelectedIndexChanged += (_, _) =>
        {
            capture?.Stop();armed = false;
            if (bluetoothPeers.SelectedItem is BluetoothPeer peer) nativeMouse?.Select(peer.Id);
            UpdateState();
        };
        Button systemQr = new() { Text = "Системная мышь по Wi-Fi + QR", AutoSize = true };
        systemQr.Click += (_, _) => { capture?.Stop();SystemMouseQrRequested = true;Close(); };
        page.Controls.Add(systemQr);
        page.Controls.Add(new Label { Text = "Если Bluetooth-адаптер не подходит, системный ввод работает через Wi-Fi + QR с беспроводной отладкой. Для Bluetooth возврат на ПК — Ctrl + Alt + F12; автоматический возврат через край телефона пока недоступен.", AutoSize = true, MaximumSize = new Size(660, 0) });
        speed.Value = (decimal)settings.Sensitivity;
        FlowLayoutPanel mouse = new() { AutoSize = true };
        mouse.Controls.Add(new Label { Text = "Скорость курсора телефона ×", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        mouse.Controls.Add(speed);
        Button normalSpeed = new() { Text = "Сбросить на 1×", AutoSize = true }; normalSpeed.Click += (_, _) => speed.Value = 1;
        mouse.Controls.Add(normalSpeed); page.Controls.Add(mouse);
        page.Controls.Add(new Label { Text = "0,25× — медленнее; 1× — обычная; 4× — быстрее. Настройка сохраняется.", AutoSize = true });
        FlowLayoutPanel navigation = new() { AutoSize = true };
        foreach (var (label, action) in new[] { ("Назад", "back"), ("Домой", "home"), ("Недавние", "recents") })
        { Button b = new() { Text = label, AutoSize = true }; b.Click += (_, _) => session?.Post(new { type = "nav", action }); navigation.Controls.Add(b); }
        page.Controls.Add(navigation);
        FlowLayoutPanel fileActions = new() { AutoSize = true }; fileActions.Controls.Add(files);
        Button dropWindow = new() { Text = "Окно файлов: отправка и приём", AutoSize = true }; dropWindow.Click += (_, _) => OpenFiles();
        fileActions.Controls.Add(dropWindow); page.Controls.Add(fileActions); page.Controls.Add(transferState); page.Controls.Add(receiveState);
        Button cancel = new() { Text = "Отменить отправку", AutoSize = true }; cancel.Click += (_, _) => transfer?.Cancel(); page.Controls.Add(cancel);
        Button pause = new() { Text = "Остановить / возобновить подключение", AutoSize = true };
        pause.Click += (_, _) => { paused = !paused; capture?.Stop(); if (paused) { ClearQr(); host.Pause(); } else host.Resume(); UpdateState(); }; page.Controls.Add(pause);
        Button forget = new() { Text = "Забыть телефон", AutoSize = true }; forget.Click += (_, _) =>
        { try { capture?.Stop(); host.Forget(); ClearQr(); UpdateState(); } catch (IOException) { state.Text = "Не удалось сохранить изменение. Попробуйте ещё раз."; } }; page.Controls.Add(forget);
        CheckBox startup = new() { Text = "Открывать этот режим при запуске Galaxy Bridge", AutoSize = true, Checked = settings.NativeAtStartup };
        startup.CheckedChanged += (_, _) => settings.NativeAtStartup = startup.Checked; page.Controls.Add(startup);
        page.Controls.Add(new Label { Text = "Ctrl + Alt + F12 — переключение. Сенсорный режим: левая кнопка — касание; правая — Назад; колесо — свайп; движение передаётся при удержании. Для него нужна служба Galaxy Bridge в специальных возможностях. Системная мышь: кнопки, наведение и прокрутка обрабатываются Android; раскладка физической клавиатуры выбирается на телефоне. Файлы: Download/GalaxyBridge; до 2 ГБ на файл.", AutoSize = true, MaximumSize = new Size(660, 0) });
        Controls.Add(page);
        renew.Click += (_, _) => { host.ClosePairing(); RefreshAddresses(); ShowQr(); }; addresses.SelectedIndexChanged += (_, _) => { if (host.PairingOpen) ShowQr(); };
        side.SelectedIndexChanged += (_, _) => { capture?.Stop(); settings.PhoneSide = (PhoneSide)side.SelectedIndex; armed = false; };
        edgeEntry.CheckedChanged += (_, _) => settings.EdgeEntryEnabled = edgeEntry.Checked;
        speed.ValueChanged += (_, _) =>
        {
            capture?.Stop(); armed = false; settings.Sensitivity = (double)speed.Value;
            try { settings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { connectionDetail.Text = "Скорость применена, но не удалось сохранить настройку. При следующем запуске задайте её повторно."; }
        };
        toggle.Click += (_, _) => Toggle(); files.Click += async (_, _) => await ChooseFilesAsync();
        host.Connected += candidate => Ui(() => Attach(candidate)); host.State += text => Ui(() => connectionDetail.Text = text);
        timer.Tick += (_, _) => { if (!host.PairingOpen) ClearQr(); if (capture?.Active == true && !InputReady) capture?.Stop(); CheckEdge(); UpdateState(); };
        clipboardRetry.Tick += (_, _) => ReadLocalClipboard();
        SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
        Shown += (_, _) => Start(); FormClosing += CloseAsync;
        RegisterDrop(this);
    }
    private void Ui(Action action)
    { if (!IsHandleCreated || closing || IsDisposed) return; try { BeginInvoke(new Action(() => { if (!closing) action(); })); } catch (InvalidOperationException) { } }
    private void Start()
    {
        try
        {
            capture = new InputCapture { PasteText = () => { try { return nativeMouse is null && share.Checked && Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch (ExternalException) { return null; } } };
            capture.ToggleRequested += Toggle; capture.EdgeReturnRequested += () => capture?.Stop();
            capture.DesktopChanged += () => Ui(() => capture?.Stop()); capture.Error += text => { state.Text = text; };
            host.Start(); RefreshAddresses(); timer.Start();
            connectionDetail.Text = "Разрешите Galaxy Bridge доступ в брандмауэре Windows для используемой сети. TCP 38271, UDP 38272.";
        }
        catch (Exception ex) when (ex is IOException or SocketException or System.ComponentModel.Win32Exception)
        { connectionDetail.Text = "Не удалось открыть режим Wi-Fi: " + ex.Message; }
    }
    private bool InputReady => nativeMouse is not null ? nativeMouse.IsAlive : session?.InputReady == true;
    private async Task StartBluetoothAsync()
    {
        capture?.Stop();armed = false;bluetooth.Enabled = false;
        BleHidSession server = new();nativeMouse = server;
        server.Changed += () => Ui(() => _ = RefreshBluetoothAsync());
        server.Fault += message => Ui(() => { capture?.Stop();bluetoothState.Text = message;UpdateState(); });
        try
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await server.StartAsync(deadline.Token);
            bluetoothStop.Visible = true;bluetoothPeers.Visible = true;
            bluetoothState.Text = "На телефоне откройте Bluetooth, найдите этот ноутбук и подтвердите сопряжение на обоих устройствах. Затем выберите телефон здесь и нажмите «Управлять телефоном». Отладка и специальные возможности для мыши не нужны. После перезапуска приложения при необходимости нажмите «Подключить» в Bluetooth телефона.";
            await RefreshBluetoothAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or OperationCanceledException or PlatformNotSupportedException)
        {
            nativeMouse = null;await server.DisposeAsync();
            bluetoothState.Text = ex is OperationCanceledException ? "Запуск Bluetooth отменён или превысил 20 секунд. Проверьте Bluetooth и повторите." : ex.Message;
            bluetooth.Enabled = !closing;bluetoothStop.Visible = bluetoothPeers.Visible = false;
        }
        UpdateState();
    }
    private async Task StopBluetoothAsync()
    {
        capture?.Stop();armed = false;
        BleHidSession? server = nativeMouse;nativeMouse = null;
        if (server is not null) await server.DisposeAsync();
        bluetoothPeers.Items.Clear();bluetoothPeers.Visible = bluetoothStop.Visible = false;bluetooth.Enabled = !closing;
        bluetoothState.Text = "Сенсорное управление через Wi-Fi. Для настоящей мыши включите системный режим.";
        UpdateState();
    }
    private async Task RefreshBluetoothAsync()
    {
        if (refreshingBluetooth) { refreshBluetoothAgain = true;return; }
        refreshingBluetooth = true;
        try
        {
            do
            {
                refreshBluetoothAgain = false;
                BleHidSession? server = nativeMouse;if (server is null || closing) return;
                string[] ids = server.Peers().Order(StringComparer.Ordinal).ToArray();
                if (ids.SequenceEqual(bluetoothPeers.Items.Cast<BluetoothPeer>().Select(p => p.Id))) continue;
                string? previous = (bluetoothPeers.SelectedItem as BluetoothPeer)?.Id;
                List<BluetoothPeer> peers = [];
                foreach (string id in ids)
                {
                    string name = "Телефон Bluetooth";
                    using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    try { name = await server.PeerNameAsync(id, deadline.Token); }
                    catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException) { }
                    peers.Add(new(id, name));
                }
                if (closing || server != nativeMouse) return;
                bluetoothPeers.Items.Clear();bluetoothPeers.Items.AddRange(peers.ToArray());
                int index = peers.FindIndex(p => p.Id == previous);
                bluetoothPeers.SelectedIndex = index >= 0 ? index : peers.Count == 1 ? 0 : -1;
                UpdateState();
            } while (refreshBluetoothAgain);
        }
        finally { refreshingBluetooth = false; }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Native.AddClipboardFormatListener(Handle)) clipboardState.Text = "Не удалось включить автоматическое чтение буфера Windows.";
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.RemoveClipboardFormatListener(Handle); base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x31D || closing) return;
        uint sequence = Native.GetClipboardSequenceNumber();
        if (sequence == ownClipboardSequence) return;
        session?.CancelClipboardPull(); clipboardAttempts = 0; ReadLocalClipboard();
    }
    private void ReadLocalClipboard()
    {
        clipboardRetry.Stop();
        if (closing || paused || suspended || !share.Checked || session is not { IsAlive: true, AutomaticClipboard: true } current) return;
        uint sequence = Native.GetClipboardSequenceNumber();
        if (sequence == ownClipboardSequence || sequence == lastPushedSequence) return;
        try
        {
            string? text = Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
            if (Native.GetClipboardSequenceNumber() != sequence) { RetryClipboard(); return; }
            if (!string.IsNullOrEmpty(text) && current.PushClipboard(text))
                clipboardState.Text = "Передаём текст с ПК в буфер телефона…";
            lastPushedSequence = sequence;
        }
        catch (ExternalException) { RetryClipboard(); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        { lastPushedSequence = sequence; clipboardState.Text = "Общий буфер поддерживает обычный текст и ссылки до 16 КБ UTF-8."; }
    }
    private void RetryClipboard()
    {
        if (++clipboardAttempts < 4) clipboardRetry.Start();
        else clipboardState.Text = "Буфер Windows занят. Повторите копирование.";
    }
    private void RefreshAddresses()
    {
        addresses.Items.Clear();
        var list = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Distinct().ToArray();
        foreach (var address in list) addresses.Items.Add(address);
        if (addresses.Items.Count > 0) addresses.SelectedIndex = 0;
    }
    private void ShowQr()
    {
        try
        {
            capture?.Stop();
            if (addresses.Items.Count == 0) RefreshAddresses();
            if (addresses.SelectedItem is not IPAddress address) { state.Text = "Подключите ноутбук к Wi-Fi и повторите."; return; }
            paused = false;
            using QRCodeGenerator generator = new(); using QRCodeData data = generator.CreateQrCode(host.NewQr(address), QRCodeGenerator.ECCLevel.M);
            using QRCode code = new(data); Image image = code.GetGraphic(6); Image? old = qr.Image; qr.Image = image; qr.Visible = true; old?.Dispose();
            state.Text = "Сканируйте в приложении Galaxy Bridge Wi-Fi. Код действует 2 минуты.";
            connectionDetail.Text = "Ожидаем соединение на " + address + ":" + LanProtocol.Port + ". После сканирования нажмите «Подключить» на S25. Если этот статус не меняется, проверьте адрес, общую сеть и брандмауэр Windows.";
        }
        catch (Exception ex) when (ex is NetworkInformationException or SocketException) { state.Text = "Не удалось определить адрес сети."; }
    }
    private void ClearQr() { Image? image = qr.Image; qr.Image = null; qr.Visible = false; image?.Dispose(); }
    private void Attach(LanSession candidate)
    {
        capture?.Stop(); session = candidate; armed = false; edgeSince = null; ClearQr(); host.ClosePairing();
        candidate.ClipboardWriter = (text, ct) => SetPhoneClipboardAsync(candidate, text, ct);
        candidate.ClipboardSequenceReader = Native.GetClipboardSequenceNumber;
        candidate.ClipboardReadAllowed = () => !closing && !paused && !suspended && candidate == session;
        candidate.AutomaticClipboardWriter = (text, sequence, ct) => SetPhoneClipboardAsync(candidate, text, ct, sequence);
        candidate.ClipboardStatus += text => Ui(() => { if (session == candidate) clipboardState.Text = text; });
        lastPushedSequence = Native.GetClipboardSequenceNumber(); clipboardRetry.Stop();
        candidate.ConfigureClipboard(share.Checked);
        if (!candidate.AutomaticClipboard) clipboardState.Text = "Для автоматического общего буфера обновите Galaxy Bridge Wi-Fi на телефоне до 0.7.6.";
        candidate.Lost += () => Ui(() => { if (session == candidate) { received = candidate.ReceiveState; capture?.Stop(); session = null; armed = false; UpdateState(); } });
        if (!candidate.IsAlive) { session = null; UpdateState(); }
    }
    private void UpdateState()
    {
        toggle.Enabled = !closing && !suspended && !paused && sending is null && InputReady && capture is not null;
        files.Enabled = !closing && !suspended && !paused && sending is null && session?.IsAlive == true;
        UpdateFiles();
        toggle.Text = capture?.Active == true ? "Вернуть курсор на ПК" : "Управлять телефоном";
        if (qr.Image is not null) return;
        if (nativeMouse is not null)
        {
            state.Text = paused ? "Подключение остановлено." : nativeMouse.IsAlive ? "Системная мышь и клавиатура готовы. " +
                (session?.IsAlive == true ? "Файлы и общий буфер подключены через Wi-Fi." : "Для файлов и общего буфера подключите приложение телефона по QR.") :
                "Ожидаем системное Bluetooth-подключение и выбор телефона. После перезапуска при необходимости нажмите «Подключить» в Bluetooth телефона.";
            return;
        }
        state.Text = paused ? "Подключение остановлено." : session?.IsAlive == true ? "Подключён: " + session.Name +
            (session.InputReady ? ". Управление готово." : ". Для управления разблокируйте экран и включите специальные возможности.") : "Ожидаем телефон. Приложение на S25 восстановит связь автоматически.";
    }
    private void Toggle()
    {
        if (capture?.Active == true) { capture.Stop(); armed = false; return; }
        if (closing || suspended || paused || sending is not null || !InputReady || capture is null) return;
        if (nativeMouse is not null) nativeMouse.ClipboardSession = session;
        IPhoneControl? current = nativeMouse is not null ? nativeMouse : session;
        if (current is null) return;
        try { capture.Start(current, settings.Sensitivity, settings.PhoneSide, true); armed = false; }
        catch (InvalidOperationException ex) { state.Text = ex.Message; }
    }
    private Task<bool> SetPhoneClipboardAsync(LanSession source, string text, CancellationToken ct, uint? expectedSequence = null)
    {
        if (closing || IsDisposed || !IsHandleCreated || ct.IsCancellationRequested) return Task.FromResult(false);
        TaskCompletionSource<bool> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Ui(async () =>
        {
            try
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (ct.IsCancellationRequested || closing || suspended || paused || source != session || !source.IsAlive || !share.Checked) return;
                    if (expectedSequence is { } expected && Native.GetClipboardSequenceNumber() != expected) return;
                    try
                    {
                        Clipboard.SetText(text, TextDataFormat.UnicodeText);
                        ownClipboardSequence = Native.GetClipboardSequenceNumber(); lastPushedSequence = ownClipboardSequence;
                        clipboardRetry.Stop();
                        clipboardState.Text = "Текст с телефона принят в буфер ПК. Вставьте через Ctrl+V.";
                        result.TrySetResult(true); return;
                    }
                    catch (ExternalException) { if (attempt < 3) await Task.Delay(50, ct); }
                }
                clipboardState.Text = "Буфер ПК занят другой программой. Повторите переход с телефона на ПК.";
            }
            catch (OperationCanceledException) { }
            finally { result.TrySetResult(false); }
        });
        return result.Task;
    }
    private void CheckEdge()
    {
        if (!edgeEntry.Checked || !toggle.Enabled || capture?.Active == true) { edgeSince = null; return; }
        Point p = Cursor.Position; Screen screen = Screen.FromPoint(p); Rectangle b = screen.Bounds;
        bool at = settings.PhoneSide switch { PhoneSide.Right => p.X >= b.Right - 1, PhoneSide.Left => p.X <= b.Left,
            PhoneSide.Top => p.Y <= b.Top, PhoneSide.Bottom => p.Y >= b.Bottom - 1, _ => false };
        if (!at) { armed = true; edgeSince = null; return; }
        if (!armed || Native.MouseButtonDown || capture?.KeyHeld == true || Native.KeyDown(0x10) || Native.KeyDown(0x11) || Native.KeyDown(0x12)) { edgeSince = null; return; }
        Point outside = settings.PhoneSide switch { PhoneSide.Right => new(b.Right, p.Y), PhoneSide.Left => new(b.Left - 1, p.Y),
            PhoneSide.Top => new(p.X, b.Top - 1), _ => new(p.X, b.Bottom) };
        if (Screen.AllScreens.Any(s => s.DeviceName != screen.DeviceName && s.Bounds.Contains(outside))) { edgeSince = null; return; }
        edgeSince ??= Environment.TickCount64;
        if (Environment.TickCount64 - edgeSince.Value >= 350) Toggle();
    }
    private async Task ChooseFilesAsync()
    {
        if (!files.Enabled) return;
        using OpenFileDialog picker = new() { Multiselect = true, Title = "Отправить на S25" };
        if (picker.ShowDialog(this) == DialogResult.OK) await SendAsync(picker.FileNames);
    }
    private void OpenFiles()
    {
        if (closing) return;
        capture?.Stop(); armed = false;
        if (fileWindow is null || fileWindow.IsDisposed)
        {
            FileDropForm window = new(SendAsync, () => transfer?.Cancel(), AllowReceive, ChooseReceiveFolder, OpenReceiveFolder, () => session?.CancelReceive()); fileWindow = window;
            window.FormClosed += (_, _) => { if (fileWindow == window) fileWindow = null; };
            UpdateFiles(); window.Show(this);
        }
        else { if (fileWindow.WindowState == FormWindowState.Minimized) fileWindow.WindowState = FormWindowState.Normal; fileWindow.Activate(); }
    }
    private void UpdateFiles()
    {
        if (session is not null) received = session.ReceiveState;
        string progress = received.Busy ? (received.Size > 0 ? $" · {received.Bytes * 100 / received.Size}%" : "") + $" · {received.Bytes / 1048576d:N1} МБ" : "";
        string text = received.Message + progress;
        if (receiveState.Text != text) receiveState.Text = text;
        fileWindow?.UpdateTransfer(files.Enabled, sending is not null,
            suspended ? "Ноутбук заблокирован или спит." : paused ? "Подключение остановлено." :
            session?.IsAlive == true ? "Телефон: " + session.Name : "Ожидаем подключение телефона…", transferState.Text);
        fileWindow?.UpdateReceive(settings.NativeReceiveEnabled, settings.NativeReceiveFolder, received);
    }
    private void SaveReceiveSettings()
    {
        session?.ConfigureReceive(settings.NativeReceiveEnabled, settings.NativeReceiveFolder);
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { connectionDetail.Text = "Настройки приёма применены, но не сохранены. Повторите их при следующем запуске."; }
        UpdateFiles();
    }
    private void AllowReceive(bool enabled) { settings.NativeReceiveEnabled = enabled; SaveReceiveSettings(); }
    private void ChooseReceiveFolder()
    {
        if (session?.ReceiveState.Busy == true) return;
        using FolderBrowserDialog picker = new() { Description = "Папка для файлов с телефона", SelectedPath = settings.NativeReceiveFolder, UseDescriptionForTitle = true };
        if (picker.ShowDialog(fileWindow ?? (Form)this) == DialogResult.OK)
        { settings.NativeReceiveFolder = picker.SelectedPath; SaveReceiveSettings(); }
    }
    private void OpenReceiveFolder()
    {
        try
        {
            Directory.CreateDirectory(settings.NativeReceiveFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(settings.NativeReceiveFolder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        { connectionDetail.Text = "Не удалось открыть папку приёма. Выберите другую папку в окне файлов."; }
    }
    private void TransferStatus(string text) { transferState.Text = text; UpdateFiles(); }
    private void RegisterDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = files.Enabled && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await SendAsync(paths); };
        foreach (Control child in control.Controls) RegisterDrop(child);
    }
    private async Task SendAsync(string[] paths)
    {
        LanSession? current = session;
        if (closing || !files.Enabled || current is null || paths.Length == 0) return;
        if (paths.Any(p => !File.Exists(p))) { TransferStatus("Выберите обычные файлы. Папки не поддерживаются."); return; }
        capture?.Stop(); armed = false;
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); transfer = cancel;
        async Task Work()
        {
            int done = 0;
            try
            {
                foreach (string path in paths)
                { TransferStatus($"Отправка {done + 1} из {paths.Length}: {Path.GetFileName(path)}"); await current.SendFileAsync(path, cancel.Token); done++; }
                TransferStatus($"Сохранено файлов: {done}. Download/GalaxyBridge.");
            }
            catch (OperationCanceledException) { TransferStatus($"Отменено. Сохранено файлов: {done}."); }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException)
            { TransferStatus($"Сохранено: {done}. Отправка остановлена: " + ex.Message); }
        }
        sending = Work(); UpdateState(); await sending; sending = null; transfer = null; if (!closing) UpdateState();
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    { if (e.Reason == SessionSwitchReason.SessionLock) Ui(Suspend); else if (e.Reason == SessionSwitchReason.SessionUnlock) Ui(Resume); }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Suspend) Ui(Suspend); else if (e.Mode == PowerModes.Resume) Ui(Resume); }
    private void Suspend() { suspended = true; armed = false; capture?.Stop(); transfer?.Cancel(); host.Pause(); ClearQr(); }
    private void Resume() { suspended = false; capture?.ResetPhysicalState(); if (!paused) host.Resume(); armed = false; }
    private async void CloseAsync(object? sender, FormClosingEventArgs e)
    {
        if (finished) return; e.Cancel = true; if (closing) return; closing = true;
        timer.Stop(); clipboardRetry.Stop(); capture?.Stop(); transfer?.Cancel(); lifetime.Cancel();
        UpdateState(); fileWindow?.Close(); fileWindow = null;
        SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
        if (sending is not null) await sending;
        if (bluetoothOperation is not null) await bluetoothOperation;
        if (nativeMouse is not null) { await nativeMouse.DisposeAsync();nativeMouse = null; }
        await host.DisposeAsync(); capture?.Dispose(); ClearQr(); timer.Dispose(); clipboardRetry.Dispose(); lifetime.Dispose();
        finished = true; shutdown.TrySetResult(); Close();
    }
}
