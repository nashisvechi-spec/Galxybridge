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
    private readonly NotifyIcon tray = new();
    private readonly PhoneControlRoute route;
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
    private readonly CheckBox fileEdge = new() { Text = "Перенос файлов с телефона через край", AutoSize = true };
    private readonly CheckBox showFileWidget = new() { Text = "Виджет отправки файлов на S25", AutoSize = true };
    private readonly CheckBox wheelZoom = new() { Text = "Масштабировать колёсиком по Wi-Fi", AutoSize = true };
    private readonly Label transferState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly Label receiveState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly CheckBox share = new() { Text = "Общий текстовый буфер", AutoSize = true };
    private readonly Label clipboardState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly Label bluetoothState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly ComboBox bluetoothPeers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, Visible = false };
    private readonly Button bluetooth = new() { Text = "Подключить Bluetooth (тест)", AutoSize = true };
    private readonly Button bluetoothStop = new() { Text = "Отключить Bluetooth", AutoSize = true, Visible = false };
    private BleHidSession? nativeMouse;
    private Task? bluetoothOperation;
    private bool refreshingBluetooth, refreshBluetoothAgain;
    private sealed record BluetoothPeer(string Id, string Name) { public override string ToString() => Name; }
    private LanReceiveState received = new(false, 0, 0, "Файлы с телефона ещё не получены.");
    private readonly NumericUpDown speed = new() { Minimum = .25M, Maximum = 4M, Increment = .05M, DecimalPlaces = 2, Width = 90 };
    private FileDropForm? fileWindow;
    private FileTransferWidget? fileWidget;
    private int? sendPercent;
    private readonly ReceiveProgressWindow receiveProgress = new(() => new ShellReceiveProgress());
    private InputCapture? capture;
    private LanSession? session;
    private CancellationTokenSource? transfer;
    private Task? sending;
    private bool closing, finished, paused, suspended, armed, fileDragging;
    private long? edgeSince;
    public LanForm(Settings settings)
    {
        Icon = AppIcon.Value;
        this.settings = settings; host = new(settings);
        route = new(() => { capture?.Stop(); armed = false; edgeSince = null; });
        Text = "Galaxy Bridge • без отладки • 0.7.23"; Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen; Size = new Size(760, Math.Min(800, (Screen.PrimaryScreen?.WorkingArea.Height ?? 850) - 40)); MinimumSize = new Size(600, 450);
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
        bluetoothState.Text = "Выбор мыши автоматический: готовая BLE HID-связь — системная мышь Android; только Wi-Fi — курсор приложения. Для текущего BLE-режима Windows должна поддерживать Bluetooth LE Peripheral.";
        bluetooth.Click += async (_, _) => await RunBluetoothAsync(true);
        bluetoothStop.Click += async (_, _) => await RunBluetoothAsync(false);
        bluetoothPeers.SelectedIndexChanged += (_, _) =>
        {
            capture?.Stop();armed = false;
            if (bluetoothPeers.SelectedItem is BluetoothPeer peer) nativeMouse?.Select(peer.Id);
            UpdateState();
        };
        page.Controls.Add(new Label { Text = "Если BLE-режим недоступен, используйте сенсорное управление через Wi-Fi. Для возврата Bluetooth-мыши через край подключите тот же телефон по QR и разрешите «Поверх других приложений». Быстрый возврат — Ctrl + Alt + F12.", AutoSize = true, MaximumSize = new Size(660, 0) });
        speed.Value = (decimal)settings.Sensitivity;
        FlowLayoutPanel mouse = new() { AutoSize = true };
        mouse.Controls.Add(new Label { Text = "Скорость курсора телефона ×", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        mouse.Controls.Add(speed);
        Button normalSpeed = new() { Text = "Сбросить на 1×", AutoSize = true }; normalSpeed.Click += (_, _) => speed.Value = 1;
        mouse.Controls.Add(normalSpeed); page.Controls.Add(mouse);
        page.Controls.Add(new Label { Text = "0,25× — медленнее; 1× — обычная; 4× — быстрее. Настройка сохраняется.", AutoSize = true });
        wheelZoom.Checked = settings.WheelZoomEnabled; page.Controls.Add(wheelZoom);
        page.Controls.Add(new Label { Text = "PDF и изображения: Ctrl + колёсико — масштабирование вокруг курсора. Включите настройку выше для масштабирования одним колёсиком. Нужен APK 0.7.13; просмотрщик должен поддерживать жест двумя пальцами. Без этой настройки колесо прокручивает страницу.", AutoSize = true, MaximumSize = new Size(660, 0) });
        wheelZoom.CheckedChanged += (_, _) =>
        {
            capture?.Stop(); armed = false; settings.WheelZoomEnabled = wheelZoom.Checked;
            if (session is not null) session.WheelZoomEnabled = wheelZoom.Checked;
            SaveSettings();
        };
        FlowLayoutPanel navigation = new() { AutoSize = true };
        foreach (var (label, action) in new[] { ("Назад", "back"), ("Домой", "home"), ("Недавние", "recents") })
        { Button b = new() { Text = label, AutoSize = true }; b.Click += (_, _) => session?.Post(new { type = "nav", action }); navigation.Controls.Add(b); }
        page.Controls.Add(navigation);
        FlowLayoutPanel fileActions = new() { AutoSize = true }; fileActions.Controls.Add(files);
        Button dropWindow = new() { Text = "Окно файлов: отправка и приём", AutoSize = true }; dropWindow.Click += (_, _) => OpenFiles();
        fileActions.Controls.Add(dropWindow); page.Controls.Add(fileActions); page.Controls.Add(transferState); page.Controls.Add(receiveState);
        fileEdge.Checked = settings.FileEdgeTransferEnabled;page.Controls.Add(fileEdge);
        fileEdge.CheckedChanged += (_, _) => {
            settings.FileEdgeTransferEnabled = fileEdge.Checked;session?.ConfigureFileEdge(fileEdge.Checked);SaveSettings();
            connectionDetail.Text = fileEdge.Checked ? "Перенос через край включён. На телефоне нужен APK 0.7.23." : "Перенос через край выключен. Обычная отправка файлов доступна.";
        };
        showFileWidget.Checked = settings.FileWidgetVisible;page.Controls.Add(showFileWidget);
        showFileWidget.CheckedChanged += (_, _) => {
            settings.FileWidgetVisible = showFileWidget.Checked;SaveSettings();
            if (showFileWidget.Checked) OpenFileWidget();else fileWidget?.Close();
        };
        Button cancel = new() { Text = "Отменить отправку", AutoSize = true }; cancel.Click += (_, _) => transfer?.Cancel(); page.Controls.Add(cancel);
        Button pause = new() { Text = "Остановить / возобновить подключение", AutoSize = true };
        pause.Click += (_, _) => ToggleConnection(); page.Controls.Add(pause);
        Button forget = new() { Text = "Забыть телефон", AutoSize = true }; forget.Click += (_, _) =>
        { try { capture?.Stop(); host.Forget(); ClearQr(); UpdateState(); } catch (IOException) { state.Text = "Не удалось сохранить изменение. Попробуйте ещё раз."; } }; page.Controls.Add(forget);
        page.Controls.Add(new Label { Text = "Ctrl + Alt + F12 — переключение. Сенсорный режим: левая кнопка — касание; правая — Назад; колесо — свайп; движение передаётся при удержании. Для него нужна служба Galaxy Bridge в специальных возможностях. Системная мышь: кнопки, наведение и прокрутка обрабатываются Android; раскладка физической клавиатуры выбирается на телефоне. Файлы: Download/GalaxyBridge; до 2 ГБ на файл.", AutoSize = true, MaximumSize = new Size(660, 0) });
        Controls.Add(page);
        renew.Click += (_, _) => { host.ClosePairing(); RefreshAddresses(); ShowQr(); }; addresses.SelectedIndexChanged += (_, _) => { if (host.PairingOpen) ShowQr(); };
        side.SelectedIndexChanged += (_, _) => { capture?.Stop(); settings.PhoneSide = (PhoneSide)side.SelectedIndex; armed = false; SaveSettings(); };
        edgeEntry.CheckedChanged += (_, _) => { settings.EdgeEntryEnabled = edgeEntry.Checked; SaveSettings(); };
        speed.ValueChanged += (_, _) =>
        {
            capture?.Stop(); armed = false; settings.Sensitivity = (double)speed.Value;
            try { settings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { connectionDetail.Text = "Скорость применена, но не удалось сохранить настройку. При следующем запуске задайте её повторно."; }
        };
        toggle.Click += (_, _) => Toggle(); files.Click += async (_, _) => await ChooseFilesAsync();
        host.Connected += candidate => Ui(() => Attach(candidate));
        host.State += text => Ui(() => {
            if (!paused && !suspended && session?.IsAlive != true) connectionDetail.Text = text;
        });
        timer.Tick += (_, _) => { if (!host.PairingOpen) ClearQr(); UpdateState(); CheckEdge(); };
        clipboardRetry.Tick += (_, _) => ReadLocalClipboard();
        SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
        Shown += async (_, _) => { Start(); if (settings.FileWidgetVisible) OpenFileWidget();await RunBluetoothAsync(true); }; FormClosing += CloseAsync;
        ContextMenuStrip menu = new();
        menu.Items.Add("Открыть", null, (_, _) => OpenWindow());
        menu.Items.Add("Управлять телефоном / вернуться на ПК", null, (_, _) => Toggle());
        menu.Items.Add("Виджет файлов на S25", null, (_, _) => { showFileWidget.Checked = true;OpenFileWidget(); });
        menu.Items.Add("Остановить / возобновить подключение", null, (_, _) => ToggleConnection());
        menu.Items.Add("Выход", null, (_, _) => Close());
        tray.Icon = AppIcon.Value; tray.Text = "Galaxy Bridge"; tray.ContextMenuStrip = menu; tray.Visible = true;
        tray.DoubleClick += (_, _) => OpenWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        RegisterDrop(this);
    }
    private void OpenWindow()
    { if (closing) return; Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void ToggleConnection()
    {
        if (closing) return;
        paused = !paused; capture?.Stop(); armed = false;
        if (paused) { ClearQr(); host.Pause(); } else if (!suspended) host.Resume();
        UpdateState();
    }
    private void SaveSettings()
    {
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { connectionDetail.Text = "Настройки применены, но не удалось сохранить их на диск."; }
    }
    private void Ui(Action action)
    { if (!IsHandleCreated || closing || IsDisposed) return; try { BeginInvoke(new Action(() => { if (!closing) action(); })); } catch (InvalidOperationException) { } }
    private void Start()
    {
        try
        {
            capture = new InputCapture { PasteText = () => { try { return route.Current is LanSession && share.Checked && Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch (ExternalException) { return null; } } };
            capture.ToggleRequested += Toggle; capture.EdgeReturnRequested += () => capture?.Stop();
            capture.DesktopChanged += () => Ui(() => capture?.Stop()); capture.Error += text => { state.Text = text; };
            host.Start(); RefreshAddresses(); timer.Start();
            connectionDetail.Text = "Разрешите Galaxy Bridge доступ в брандмауэре Windows для используемой сети. TCP 38271, UDP 38272.";
        }
        catch (Exception ex) when (ex is IOException or SocketException or System.ComponentModel.Win32Exception)
        { connectionDetail.Text = "Не удалось открыть режим Wi-Fi: " + ex.Message; }
    }
    private bool InputReady => route.Current is not null;
    private async Task RunBluetoothAsync(bool enable)
    {
        if (closing || bluetoothOperation is not null) return;
        bluetoothOperation = enable ? StartBluetoothAsync() : StopBluetoothAsync();
        try { await bluetoothOperation; } finally { bluetoothOperation = null; }
    }
    private async Task StartBluetoothAsync()
    {
        if (nativeMouse is not null) return;
        bluetooth.Enabled = false;
        BleHidSession server = new();nativeMouse = server;
        server.Changed += () => Ui(() => { if (nativeMouse == server) { UpdateState(); _ = RefreshBluetoothAsync(); } });
        server.Fault += message => Ui(() => { if (nativeMouse == server) { bluetoothState.Text = message;UpdateState(); } });
        try
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await server.StartAsync(deadline.Token);
            bluetoothStop.Visible = true;bluetoothPeers.Visible = true;
            bluetoothState.Text = "На телефоне откройте Bluetooth, найдите ноутбук и подтвердите сопряжение. Единственный подключённый телефон выбирается автоматически; если их несколько, выберите нужный. При готовой HID-связи используется системная мышь, иначе — мышь приложения по Wi-Fi. После перезапуска при необходимости нажмите «Подключить» в Bluetooth телефона.";
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
        bluetoothState.Text = "Bluetooth отключён. При подключении по Wi-Fi используется курсор приложения.";
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
        if (!host.IsCurrent(candidate) || paused || suspended) return;
        connectionDetail.Text = "Связь с телефоном установлена. Файлы и управление доступны.";
        candidate.WheelZoomEnabled = settings.WheelZoomEnabled;
        capture?.Stop(); session = candidate; armed = false; edgeSince = null; ClearQr(); host.ClosePairing();
        candidate.ClipboardWriter = (text, ct) => SetPhoneClipboardAsync(candidate, text, ct);
        candidate.ClipboardSequenceReader = Native.GetClipboardSequenceNumber;
        candidate.ClipboardReadAllowed = () => !closing && !paused && !suspended && candidate == session;
        candidate.AutomaticClipboardWriter = (text, sequence, ct) => SetPhoneClipboardAsync(candidate, text, ct, sequence);
        candidate.ClipboardStatus += text => Ui(() => { if (session == candidate) clipboardState.Text = text; });
        candidate.NativeEdgeStatus += text => Ui(() => { if (session == candidate) bluetoothState.Text = text; });
        candidate.FileDragRequested += (batch, sample) => Ui(() => StartPhoneFileDrag(candidate, batch, sample));
        lastPushedSequence = Native.GetClipboardSequenceNumber(); clipboardRetry.Stop();
        candidate.ConfigureClipboard(share.Checked);
        candidate.ConfigureFileEdge(settings.FileEdgeTransferEnabled);
        if (!candidate.AutomaticClipboard) clipboardState.Text = "Для автоматического общего буфера обновите Galaxy Bridge Wi-Fi на телефоне до 0.7.13.";
        candidate.Lost += () => Ui(() => { if (session == candidate) {
            received = candidate.ReceiveState; capture?.Stop(); session = null; armed = false;
            if (!paused && !suspended) connectionDetail.Text = "Связь прервалась. Ожидаем автоматическое переподключение телефона. Незавершённую передачу файлов нужно повторить.";
            UpdateState();
        } });
        if (!candidate.IsAlive) {
            session = null;
            connectionDetail.Text = "Связь прервалась. Ожидаем автоматическое переподключение телефона.";
            UpdateState();
        }
    }
    private void UpdateState()
    {
        route.Update(nativeMouse, session);
        toggle.Enabled = !fileDragging && !closing && !suspended && !paused && sending is null && InputReady && capture is not null;
        files.Enabled = !fileDragging && !closing && !suspended && !paused && sending is null && session?.IsAlive == true;
        UpdateFiles();
        toggle.Text = capture?.Active == true ? "Вернуть курсор на ПК" : "Управлять телефоном";
        if (qr.Image is not null) return;
        if (route.Current is BleHidSession)
        {
            state.Text = paused ? "Подключение остановлено." : "Мышь: системная, по Bluetooth. " +
                (session?.IsAlive == true ? "Файлы и общий буфер подключены через Wi-Fi." : "Для файлов и общего буфера подключите приложение телефона по QR.");
            return;
        }
        state.Text = paused ? "Подключение остановлено." : session?.IsAlive == true ? "Подключён: " + session.Name +
            (session.InputReady ? ". Мышь: курсор приложения, по Wi-Fi." : ". Для управления разблокируйте экран и включите специальные возможности.") : "Ожидаем телефон по Wi-Fi или Bluetooth.";
    }
    private void Toggle()
    {
        if (fileDragging) return;
        route.Update(nativeMouse, session);
        if (capture?.Active == true) { capture.Stop(); armed = false; return; }
        if (closing || suspended || paused || sending is not null || !InputReady || capture is null) return;
        if (nativeMouse is not null) nativeMouse.ClipboardSession = session;
        IPhoneControl? current = route.Current;
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
        if (fileDragging || !edgeEntry.Checked || !toggle.Enabled || capture?.Active == true) { edgeSince = null; return; }
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
    private void StartPhoneFileDrag(LanSession source, string batch, PhoneEdgeSample sample)
    {
        if (fileDragging) return;
        bool copied = false, ownsDrag = false; string message = "Перетаскивание отменено. Подготовьте файлы заново.";
        string[] paths = [];
        QueryContinueDragEventHandler guard = (_, e) =>
        {
            if (e.EscapePressed || closing || paused || suspended || session != source || !source.IsAlive || !settings.NativeReceiveEnabled || !settings.FileEdgeTransferEnabled)
                e.Action = DragAction.Cancel;
            // Phone capture consumed the original Windows DOWN. Use the real hook's UP,
            // not Windows' missing button state; never inject a synthetic click on the desktop.
            else e.Action = capture?.FileDragButtonHeld == true ? DragAction.Continue : DragAction.Drop;
        };
        try
        {
            if (fileDragging || closing || paused || suspended || session != source || !source.IsAlive || sending is not null ||
                !settings.NativeReceiveEnabled || !settings.FileEdgeTransferEnabled || capture?.HandoffFileDrag(source, sample) != true)
            { message = "Зажмите подготовленные файлы мышью и тяните к краю ПК, не отпуская кнопку."; return; }
            armed = false; edgeSince = null;
            paths = source.TakeDragFiles(batch);
            if (capture?.FileDragButtonHeld != true) return;
            fileDragging = ownsDrag = true; UpdateState();
            DataObject data = new();
            data.SetData(DataFormats.FileDrop, paths);
            // Copy only: originals on the phone are never removed.
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
            QueryContinueDrag += guard;
            receiveState.Text = "Отпустите файлы на рабочем столе или в папке Проводника. Esc — отмена.";
            copied = DoDragDrop(data, DragDropEffects.Copy) == DragDropEffects.Copy;
            message = copied ? "Файлы переданы Windows в выбранное место." : "Перетаскивание отменено. Оригиналы на телефоне сохранены.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or InvalidOperationException)
        { message = "Перетаскивание не выполнено. Подготовьте файлы заново."; }
        finally
        {
            QueryContinueDrag -= guard;
            capture?.EndFileDrag();
            if (ownsDrag) { fileDragging = false; armed = false; if (!closing) UpdateState(); }
            if (paths.Length > 0 && !copied) LanDragStaging.DeleteOwnedFolder(Path.GetDirectoryName(paths[0])!);
            // Explorer can copy asynchronously after OLE returns. Successful sources remain in the
            // private cache for 24 hours and are removed on a later start, never during shell copying.
            if (paths.Length > 0 && copied)
            {
                try { Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(paths[0])!, DateTime.UtcNow); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            transferState.Text = message; receiveState.Text = message;
            source.Post(new { type = "dragResult", batch, ok = copied, error = copied ? "" : message });
        }
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
    private void OpenFileWidget()
    {
        if (closing) return;
        if (fileWidget is { IsDisposed: false }) { fileWidget.Show();fileWidget.Activate();return; }
        FileTransferWidget widget = new(SendAsync, () => transfer?.Cancel());fileWidget = widget;
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1000, 700);
        Point wanted = settings.FileWidgetX is int x && settings.FileWidgetY is int y ? new Point(x, y) :
            new Point(area.Right-widget.Width-16, area.Bottom-widget.Height-16);
        Screen screen = Screen.FromPoint(wanted);area = screen.WorkingArea;
        widget.Location = new Point(Math.Clamp(wanted.X, area.Left, Math.Max(area.Left, area.Right-widget.Width)),
            Math.Clamp(wanted.Y, area.Top, Math.Max(area.Top, area.Bottom-widget.Height)));
        widget.FormClosed += (_, _) => {
            settings.FileWidgetX = widget.Left;settings.FileWidgetY = widget.Top;
            if (fileWidget == widget) fileWidget = null;
            if (!closing) { showFileWidget.Checked = false;settings.FileWidgetVisible = false;SaveSettings(); }
        };
        UpdateFiles();widget.Show(); // No owner: minimizing the main window must not hide the widget.
    }
    private void UpdateFiles()
    {
        LanSession? current = !closing && !paused && !suspended && !fileDragging && session?.IsAlive == true ? session : null;
        var progressSnapshot = current?.ReceiveProgress;
        receiveProgress.Update(current, progressSnapshot?.State ?? received, Handle,
            id => current?.CancelReceive(id, progressSnapshot?.Staging == true));
        if (session is not null) received = session.ReceiveState;
        string progress = received.Busy ? (received.Size > 0 ? $" · {received.Bytes * 100 / received.Size}%" : "") + $" · {received.Bytes / 1048576d:N1} МБ" : "";
        string text = received.Message + progress;
        if (receiveState.Text != text) receiveState.Text = text;
        fileWindow?.UpdateTransfer(files.Enabled, sending is not null,
            suspended ? "Ноутбук заблокирован или спит." : paused ? "Подключение остановлено." :
            session?.IsAlive == true ? "Телефон: " + session.Name : "Ожидаем подключение телефона…", transferState.Text);
        fileWindow?.UpdateReceive(settings.NativeReceiveEnabled, settings.NativeReceiveFolder, received);
        fileWidget?.UpdateTransfer(files.Enabled, sending is not null,
            suspended ? "Ноутбук заблокирован или спит." : paused ? "Подключение остановлено." :
            session?.IsAlive == true ? "Телефон: " + session.Name : "Ожидаем подключение S25…", transferState.Text, sendPercent);
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
        if (paths.Length > 100 || paths.Any(p => !File.Exists(p))) { TransferStatus("Выберите до 100 обычных файлов. Папки не поддерживаются."); return; }
        capture?.Stop(); armed = false;
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); transfer = cancel;
        sendPercent = null;
        async Task Work()
        {
            int done = 0;
            try
            {
                foreach (string path in paths)
                {
                    int number = done + 1;string label = $"Отправка {number} из {paths.Length}: {Path.GetFileName(path)}";
                    sendPercent = null;TransferStatus(label);
                    Progress<(long Sent, long Total)> progress = new(value => {
                        if (transfer != cancel || done != number-1 || closing) return;
                        sendPercent = value.Total > 0 ? (int)Math.Min(100, value.Sent*100/value.Total) : null;
                        TransferStatus(label + (sendPercent is int percent ? $" · {percent}%" : ""));
                    });
                    await current.SendFileAsync(path, cancel.Token, progress);done++;
                }
                TransferStatus($"Сохранено файлов: {done}. Download/GalaxyBridge.");
            }
            catch (OperationCanceledException) { TransferStatus($"Отменено. Сохранено файлов: {done}."); }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException)
            { TransferStatus($"Сохранено: {done}. Отправка остановлена: " + ex.Message); }
        }
        sending = Work(); UpdateState(); await sending; sending = null; transfer = null;sendPercent = null; if (!closing) UpdateState();
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
        timer.Stop(); clipboardRetry.Stop(); receiveProgress.Dispose(); capture?.Stop(); transfer?.Cancel(); lifetime.Cancel();
        UpdateState(); fileWindow?.Close(); fileWindow = null;fileWidget?.Close();fileWidget = null;
        SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
        if (sending is not null) await sending;
        if (bluetoothOperation is not null) await bluetoothOperation;
        if (nativeMouse is not null) { await nativeMouse.DisposeAsync();nativeMouse = null; }
        await host.DisposeAsync(); capture?.Dispose(); ClearQr(); timer.Dispose(); clipboardRetry.Dispose(); lifetime.Dispose();
        SaveSettings(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
        finished = true; Close();
    }
}
