using System.Runtime.InteropServices;
using System.Text;
using GalaxyBridge.Core;
using Microsoft.Win32;

namespace GalaxyBridge.Windows;

internal sealed class MainForm : Form
{
    private readonly AdbClient adb = new();
    private readonly Settings settings = Settings.Load();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConnectionRecovery recovery = new();
    private readonly DeviceDiscovery discovery;
    private InputCapture? capture;
    private PhoneSession? session;
    private CancellationTokenSource? operation;
    private Task? pendingOperation, pendingDisconnect;
    private bool busy, disconnecting, closing, closed, edgeArmed = true;
    private bool automaticOperation, desktopLocked, sleeping;
    private string? lastAutoError;
    private string? cachedClipboard;
    private DateTime? edgeSince;
    private readonly ComboBox devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown speed = new() { Minimum = .25M, Maximum = 4M, Increment = .25M, DecimalPlaces = 2, Dock = DockStyle.Fill };
    private readonly CheckBox share = new() { Text = "Общий текстовый буфер", AutoSize = true };
    private readonly CheckBox edge = new() { Text = "Вход на телефон через край экрана", AutoSize = true };
    private readonly CheckBox edgeReturn = new() { Text = "Автовозврат через край телефона", AutoSize = true };
    private readonly CheckBox autoConnect = new() { Text = "Автоподключение к последнему телефону при запуске", AutoSize = true };
    private readonly CheckBox reconnect = new() { Text = "Восстанавливать связь после обрыва", AutoSize = true };
    private readonly Label remembered = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    private readonly TextBox wifi = new() { PlaceholderText = "192.168.1.10:37121", Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(660, 0), Padding = new Padding(0, 12, 0, 12) };
    private readonly TextBox journal = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, ForeColor = SystemColors.ControlText };
    private readonly TabPage filesPage = new("Файлы");
    private readonly Label transferStatus = new() { AutoSize = true, MaximumSize = new Size(620, 0) };
    private readonly ProgressBar transferProgress = new() { Dock = DockStyle.Fill, Height = 20, Visible = false };
    private readonly Button refresh = Button("Найти устройства"), connect = Button("Подключить выбранный"),
        disconnect = Button("Отключить"), toggle = Button("Управлять телефоном"), pair = Button("Сопряжение Wi-Fi"),
        wifiConnect = Button("Подключиться по Wi-Fi"), sendFile = Button("Выбрать файлы…"), layout = Button("Раскладка клавиатуры");
    private readonly Button pairQr = Button("Сопряжение по QR-коду"), forget = Button("Забыть последний телефон");
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Visible = true, Text = "Galaxy Bridge" };
    private readonly System.Windows.Forms.Timer edgeTimer = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer clipboardRetry = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer autoTimer = new() { Interval = 1000 };
    private int clipboardRetries;

    public MainForm()
    {
        discovery = new(adb);
        Text = "Galaxy Bridge • Windows 10"; StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, Math.Min(760, (Screen.PrimaryScreen?.WorkingArea.Height ?? 800) - 50));
        MinimumSize = new Size(640, 480); BackColor = Color.FromArgb(18, 24, 38);
        ForeColor = Color.FromArgb(232, 237, 247); Font = new Font("Segoe UI", 10);
        BuildUi();
        RegisterFileDrop(this);
        side.Items.AddRange(["Справа", "Слева", "Сверху", "Снизу"]);
        side.SelectedIndex = (int)settings.PhoneSide;
        speed.Value = (decimal)settings.Sensitivity; share.Checked = settings.ClipboardEnabled; edge.Checked = settings.EdgeEntryEnabled;
        edgeReturn.Checked = settings.EdgeReturnEnabled;
        autoConnect.Checked = settings.AutoConnectEnabled; reconnect.Checked = settings.ReconnectEnabled;
        UpdateRemembered();
        refresh.Click += async (_, _) => await RunAsync(RefreshAsync);
        connect.Click += async (_, _) => await RunAsync(ConnectAsync);
        pair.Click += async (_, _) => await RunAsync(PairAsync);
        pairQr.Click += async (_, _) => await RunAsync(PairQrAsync);
        wifiConnect.Click += async (_, _) => await RunAsync(ConnectWifiAsync);
        disconnect.Click += async (_, _) =>
        {
            if (busy) { if (automaticOperation || session is null) recovery.Pause(); operation?.Cancel(); }
            else await DisconnectAsync();
        };
        forget.Click += (_, _) => { recovery.Pause(); settings.LastPhone = null; SaveSettings(); UpdateRemembered(); UpdateButtons(); SetStatus("Последний телефон забыт. Выберите устройство для нового подключения."); };
        toggle.Click += (_, _) => ToggleCapture();
        sendFile.Click += async (_, _) => await RunAsync(SendFileAsync);
        layout.Click += (_, _) => session?.Send([15]);
        side.SelectedIndexChanged += (_, _) => { StopCapture(); settings.PhoneSide = (PhoneSide)side.SelectedIndex; edgeSince = null; };
        speed.ValueChanged += (_, _) => { settings.Sensitivity = (double)speed.Value; if (capture?.Active == true) StopCapture(); };
        share.CheckedChanged += (_, _) => settings.ClipboardEnabled = share.Checked;
        edge.CheckedChanged += (_, _) => { settings.EdgeEntryEnabled = edge.Checked; edgeSince = null; };
        edgeReturn.CheckedChanged += (_, _) => { StopCapture(); settings.EdgeReturnEnabled = edgeReturn.Checked; };
        autoConnect.CheckedChanged += (_, _) => { settings.AutoConnectEnabled = autoConnect.Checked; AutomationChanged(autoConnect.Checked); };
        reconnect.CheckedChanged += (_, _) => { settings.ReconnectEnabled = reconnect.Checked; AutomationChanged(reconnect.Checked); };
        edgeTimer.Tick += (_, _) => CheckEdge();
        clipboardRetry.Tick += (_, _) => { if (++clipboardRetries > 3) clipboardRetry.Stop(); else ReadClipboard(); };
        autoTimer.Tick += async (_, _) => await TryAutomaticAsync();
        tray.DoubleClick += (_, _) => OpenWindow();
        ContextMenuStrip menu = new();
        menu.Items.Add("Открыть", null, (_, _) => OpenWindow());
        menu.Items.Add("Переключить управление", null, (_, _) => ToggleCapture());
        menu.Items.Add("Отключить телефон", null, async (_, _) => await DisconnectAsync());
        menu.Items.Add("Выход", null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Shown += OnShown;
        FormClosing += OnClosing;
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerChanged;
        UpdateButtons();
    }

    private static Button Button(string text) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new Size(150, 38), FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(57, 69, 113), ForeColor = Color.White, Margin = new Padding(0, 6, 8, 6)
    };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 10, 0, 8), MaximumSize = new Size(620, 0) };
    private static TableLayoutPanel Page() => new()
    { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(16), BackColor = Color.FromArgb(24, 32, 49), ForeColor = Color.FromArgb(232, 237, 247) };
    private static void Add(TableLayoutPanel page, Control control)
    { int row = page.RowCount++; page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.Controls.Add(control, 0, row); }
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        FlowLayoutPanel row = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        row.Controls.AddRange(controls); return row;
    }
    private void BuildUi()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label title = Label("Galaxy Bridge"); title.Font = new Font("Segoe UI Semibold", 24); title.Margin = Padding.Empty;
        root.Controls.Add(title, 0, 0); root.Controls.Add(status, 0, 1);
        TableLayoutPanel connection = Page(), controls = Page(), files = Page(), help = Page();
        TabPage connectPage = new("Подключение"), controlPage = new("Управление"), helpPage = new("Помощь и журнал");
        connectPage.Controls.Add(connection); controlPage.Controls.Add(controls); helpPage.Controls.Add(help);
        filesPage.Controls.Add(files);
        tabs.TabPages.AddRange([connectPage, controlPage, filesPage, helpPage]); root.Controls.Add(tabs, 0, 2);
        Add(connection, Label("Телефон через USB или Wi-Fi")); Add(connection, devices); Add(connection, Row(refresh, connect));
        Add(connection, Label("USB: подключите S25 кабелем для передачи данных и разрешите отладку на телефоне."));
        Add(connection, Label("Wi-Fi: включите «Беспроводная отладка». Для первого подключения используйте QR-код или код сопряжения."));
        Add(connection, Row(pairQr, pair)); Add(connection, Label("IP-адрес и порт подключения с основной страницы «Беспроводная отладка»:"));
        Add(connection, wifi); Add(connection, wifiConnect);
        Add(connection, autoConnect); Add(connection, reconnect); Add(connection, remembered); Add(connection, forget);
        Add(connection, Label("При восстановлении связи курсор остаётся на ноутбуке. «Отключить» приостанавливает автоматические попытки до нового подключения или перезапуска программы."));
        Add(controls, Label("Расположение телефона относительно монитора")); Add(controls, side);
        Add(controls, edge); Add(controls, edgeReturn);
        Add(controls, Label("Вход: задержите курсор у края ноутбука на 350 мс. Возврат: двигайте его через край телефона в сторону ноутбука. Отпустите кнопки и клавиши. Горячая клавиша остаётся запасным способом."));
        Add(controls, Label("Скорость мыши")); Add(controls, speed); Add(controls, share);
        Add(controls, Label("Ctrl + C / Ctrl + X на телефоне передают текст ноутбуку. Ctrl + V вставляет текст с ноутбука на телефон."));
        Add(controls, layout);
        Add(controls, Label("Русский ввод: один раз выберите русскую и английскую раскладки для Galaxy Bridge Keyboard в настройках телефона."));
        Label drop = Label("Перетащите сюда файлы из Проводника\nили выберите их кнопкой ниже.");
        drop.AutoSize = false; drop.Dock = DockStyle.Fill; drop.Height = 110;
        drop.TextAlign = ContentAlignment.MiddleCenter; drop.BorderStyle = BorderStyle.FixedSingle;
        Add(files, drop); Add(files, sendFile);
        Add(files, Label("Отправка на подключённый телефон через USB или Wi-Fi. Файлы копируются; оригиналы остаются на ноутбуке. Папки пока не поддерживаются."));
        Add(files, Label("На S25: «Мои файлы → Внутренняя память → Download → GalaxyBridge». Все отправки сохраняются здесь. Одноимённые файлы получают суффиксы (2), (3)."));
        Add(files, transferProgress); Add(files, transferStatus);
        Add(files, Label("Во время передачи управление остаётся на ноутбуке. Кнопка «Отменить» останавливает отправку; уже переданные файлы сохраняются."));
        Add(help, Label("Ctrl + Alt + F12 — переключение управления. На S25 при необходимости: Ctrl + Alt + Fn + F12 на ноутбуке."));
        Add(help, Label("Экран телефона должен быть разблокирован. Изображение остаётся на S25. Файлы сохраняются в папку Download. Буфер поддерживает текст. Удерживать экран включённым программа не заставляет."));
        Add(help, Label("Автовозврат: отправьте GalaxyBridgeEdge.apk из папки сборки через вкладку «Файлы». Установите APK из Download/GalaxyBridge, откройте Galaxy Bridge Edge и разрешите показ поверх других приложений. Затем переподключите телефон в программе."));
        Add(help, Label("Это тестовая версия самостоятельного приложения. На реальной связке HP + S25 её нужно проверить после сборки."));
        journal.MinimumSize = new Size(0, 160); Add(help, journal);
        Button instructions = Button("Открыть инструкцию"); instructions.Click += (_, _) => OpenInstructions(); Add(help, instructions);
        root.Controls.Add(Row(toggle, disconnect), 0, 3); Controls.Add(root);
        SetStatus("Подключите Samsung Galaxy S25.");
    }

    private async void OnShown(object? sender, EventArgs args)
    {
        try
        {
            capture = new InputCapture { PasteText = () => share.Checked ? cachedClipboard : null };
            capture.ToggleRequested += ToggleCapture;
            capture.DesktopChanged += () => Ui(SuspendCapture);
            capture.EdgeReturnRequested += () => { StopCapture(); SetStatus("Курсор вернулся на ноутбук через край телефона."); };
            capture.Error += text => Ui(() => { StopCapture(); SetStatus(text, true); });
            _ = Native.AddClipboardFormatListener(Handle); ReadClipboard(); edgeTimer.Start();
            await RunAsync(RefreshAsync);
            if (!closing) { autoTimer.Start(); await TryAutomaticAsync(); }
        }
        catch (Exception e) { SetStatus(e.Message, true); }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x31D) { clipboardRetries = 0; ReadClipboard(); }
        base.WndProc(ref m);
    }
    private void ReadClipboard()
    {
        if (closing) return;
        try
        {
            string? text = Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
            cachedClipboard = text is not null && Encoding.UTF8.GetByteCount(text) <= ControlProtocol.MaxClipboardBytes ? text : null;
            clipboardRetry.Stop();
        }
        catch (ExternalException) { cachedClipboard = null; clipboardRetry.Start(); }
    }
    private void SetPhoneClipboard(PhoneSession source, string text)
    {
        Ui(() =>
        {
            if (source != session || !share.Checked || closing) return;
            try
            {
                if (text.Length == 0) Clipboard.Clear();
                else Clipboard.SetText(text, TextDataFormat.UnicodeText);
                ReadClipboard();
            }
            catch (ExternalException) { SetStatus("Буфер занят другой программой. Повторите копирование.", true); }
        });
    }
    private void Ui(Action action)
    {
        if (closing || IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        { try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        else action();
    }
    private void Log(string text)
    {
        Ui(() =>
        {
            string safe = text.Replace('\r', ' ').Replace('\n', ' ');
            string[] old = journal.Lines.TakeLast(79).ToArray();
            journal.Lines = [.. old, DateTime.Now.ToString("HH:mm:ss") + "  " + safe];
            journal.SelectionStart = journal.TextLength; journal.ScrollToCaret();
        });
    }
    private void SetStatus(string text, bool error = false)
    { status.Text = text; status.ForeColor = error ? Color.FromArgb(255, 161, 155) : Color.FromArgb(140, 221, 208); }
    private void UpdateButtons()
    {
        bool attached = session is not null;
        bool available = !disconnecting && !closing;
        bool waiting = settings.LastPhone is not null && !recovery.Paused &&
            (recovery.Recovering ? settings.ReconnectEnabled : settings.AutoConnectEnabled);
        refresh.Enabled = connect.Enabled = pair.Enabled = pairQr.Enabled = wifiConnect.Enabled = !busy && !attached && available;
        forget.Enabled = !busy && available && settings.LastPhone is not null;
        devices.Enabled = !busy && !attached && available;
        toggle.Enabled = !busy && attached && capture is not null && available;
        sendFile.Enabled = CanSendFiles;
        layout.Enabled = !busy && attached && available;
        disconnect.Enabled = (busy || attached || waiting) && available;
        disconnect.Text = busy ? "Отменить" : attached ? "Отключить" : waiting ? "Остановить автоподключение" : "Отключить";
        toggle.Text = capture?.Active == true ? "Вернуть управление ноутбуку" : "Управлять телефоном";
    }
    private async Task RunAsync(Func<CancellationToken, Task> action, bool automatic = false)
    {
        if (busy || disconnecting || closing) return;
        StopCapture(); busy = true; automaticOperation = automatic;
        using CancellationTokenSource current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = current; UpdateButtons();
        try { pendingOperation = InvokeActionAsync(action, current.Token); await pendingOperation; }
        catch (OperationCanceledException) { if (!closing) SetStatus("Операция отменена."); }
        catch (Exception e)
        {
            if (!closing)
            {
                if (automatic) AutomaticFailed(e);
                else { SetStatus(e.Message, true); Log("Операция не выполнена: " + e.GetType().Name); }
            }
        }
        finally { pendingOperation = null; operation = null; busy = automaticOperation = false; if (!closing) UpdateButtons(); }
    }
    private static async Task InvokeActionAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    { await Task.Yield(); ct.ThrowIfCancellationRequested(); await action(ct); }
    private async Task RefreshAsync(CancellationToken ct)
    {
        IReadOnlyList<AdbDevice> list = await adb.DevicesAsync(ct);
        devices.Items.Clear(); foreach (AdbDevice device in list) devices.Items.Add(device);
        if (devices.Items.Count > 0) devices.SelectedIndex = list.Select((d, i) => (d, i)).FirstOrDefault(x => x.d.Ready).i;
        SetStatus(list.Any(d => d.Ready) ? "Телефон найден. Выберите устройство и подключитесь."
            : list.Any(d => d.State == "unauthorized") ? "Разблокируйте S25 и разрешите отладку для этого ноутбука."
            : "Доступные телефоны не найдены. Проверьте кабель и настройки отладки.");
    }
    private async Task ConnectAsync(CancellationToken ct)
    {
        if (devices.SelectedItem is not AdbDevice device) throw new InvalidOperationException("Сначала найдите и выберите телефон.");
        if (!device.Ready) throw new InvalidOperationException("Телефон не готов. Разрешите отладку и повторите поиск устройств.");
        recovery.Pause();
        await ConnectDeviceAsync(device, ct);
    }
    private async Task ConnectDeviceAsync(AdbDevice device, CancellationToken ct, PhoneIdentity? verified = null, string endpoint = "")
    {
        SetStatus("Подключаемся к телефону…");
        PhoneIdentity identity = verified ?? new("", "");
        if (verified is null)
        {
            try { identity = await adb.IdentityAsync(device.Serial, ct); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { Log("Идентификатор телефона недоступен; автоматическое подключение не будет сохранено."); }
        }
        PhoneSession candidate = new(adb, device.Serial, Log);
        candidate.ClipboardReceived += text => SetPhoneClipboard(candidate, text);
        candidate.AutoReturnUnavailable += text => Ui(() => { if (session == candidate && capture?.Active == true) SetStatus(text, true); });
        candidate.ConnectionLost += text => Ui(async () =>
        {
            if (session == candidate)
            {
                StopCapture(); recovery.Lost(Environment.TickCount64); await DisconnectAsync(manual: false);
                if (!closing) SetStatus(text + (settings.ReconnectEnabled && settings.LastPhone is not null && !recovery.Paused
                    ? " Ожидаем телефон для восстановления связи…" : ""), true);
            }
        });
        try
        {
            await candidate.ConnectAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (!candidate.IsAlive) throw new IOException("Соединение завершилось при подключении.");
            session = candidate;
            RememberPhone(device, identity, endpoint); recovery.Succeeded(); lastAutoError = null; edgeArmed = false;
            SetStatus(candidate.AutoReturnReady
                ? $"{candidate.DeviceName} подключён. Автовозврат готов; Ctrl + Alt + F12 передаст управление."
                : $"{candidate.DeviceName} подключён. Автовозврат недоступен — см. журнал и инструкцию по APK. Ctrl + Alt + F12 передаст управление.");
            Log("Подключено устройство. Видео и звук не передаются.");
        }
        catch { if (session == candidate) session = null; await candidate.DisposeAsync(); throw; }
    }
    private async Task DisconnectAsync(bool manual = true)
    {
        if (manual) recovery.Pause();
        if (pendingDisconnect is not null) { await pendingDisconnect; return; }
        operation?.Cancel(); StopCapture();
        PhoneSession? previous = session; session = null; disconnecting = true; UpdateButtons();
        try
        {
            if (previous is not null) { pendingDisconnect = previous.DisposeAsync().AsTask(); await pendingDisconnect; }
            if (!closing) SetStatus("Телефон отключён. Управление ноутбуком.");
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        { if (!closing) SetStatus("Связь закрыта. Не удалось завершить очистку: " + ex.GetType().Name, true); }
        finally { pendingDisconnect = null; disconnecting = false; if (!closing) UpdateButtons(); }
    }
    private void SaveSettings()
    {
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Log("Настройки не удалось сохранить: " + ex.GetType().Name); }
    }
    private void UpdateRemembered() => remembered.Text = settings.LastPhone is { } phone
        ? "Последний телефон: " + phone.Model : "Последний телефон появится после первого подключения.";
    private void RememberPhone(AdbDevice device, PhoneIdentity identity, string endpoint)
    {
        RememberedPhone? previous = settings.LastPhone;
        bool same = previous?.Identity.Matches(identity) == true;
        if (endpoint.Length == 0)
        { try { endpoint = AdbEndpoint.Parse(device.Serial).ToString(); } catch (FormatException) { } }
        if (same && previous is not null)
        {
            if (endpoint.Length == 0) endpoint = previous.WifiEndpoint;
            identity = new(identity.HardwareSerial.Length > 0 ? identity.HardwareSerial : previous.Identity.HardwareSerial,
                identity.WifiGuid.Length > 0 ? identity.WifiGuid : previous.Identity.WifiGuid);
        }
        string model = device.Model.Length <= 128 ? device.Model : device.Model[..128];
        settings.LastPhone = identity.Known ? new(identity, model, device.Serial, endpoint) : null;
        SaveSettings(); UpdateRemembered();
    }
    private void AutomationChanged(bool enabled)
    {
        SaveSettings();
        if (enabled) recovery.Resume(Environment.TickCount64);
        else if (automaticOperation && !(recovery.Recovering ? reconnect.Checked : autoConnect.Checked)) operation?.Cancel();
        UpdateButtons();
    }
    private void AutomaticFailed(Exception? error)
    {
        recovery.Failed(Environment.TickCount64);
        SetStatus(recovery.Recovering ? "Связь пока не восстановлена. Ожидаем последний телефон…"
            : "Последний телефон пока недоступен. Ожидаем USB или Wi-Fi…");
        string reason = error?.GetType().Name ?? "NotFound";
        if (reason != lastAutoError) { lastAutoError = reason; Log("Автоподключение: " + reason + ". Повторяем поиск с паузой до 30 секунд."); }
    }
    private async Task TryAutomaticAsync()
    {
        if (settings.LastPhone is not { Valid: true } phone ||
            !recovery.Due(settings.AutoConnectEnabled, settings.ReconnectEnabled, desktopLocked || sleeping,
                session is not null, busy || disconnecting || closing, Environment.TickCount64)) return;
        await RunAsync(async ct =>
        {
            DiscoveredPhone? found;
            using (CancellationTokenSource search = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                search.CancelAfter(TimeSpan.FromSeconds(12));
                try { found = await discovery.FindRememberedAsync(phone, search.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new TimeoutException("Поиск последнего телефона завершён по таймауту."); }
            }
            ct.ThrowIfCancellationRequested();
            if (found is null) { AutomaticFailed(null); return; }
            devices.Items.Clear(); devices.Items.Add(found.Device); devices.SelectedIndex = 0;
            await ConnectDeviceAsync(found.Device, ct, found.Identity, found.WifiEndpoint);
            Log("Автоматическое подключение выполнено. Управление остаётся на ноутбуке.");
        }, automatic: true);
    }
    private async Task ConnectWifiAsync(CancellationToken ct)
    {
        recovery.Pause();
        AdbEndpoint endpoint = AdbEndpoint.Parse(wifi.Text);
        await adb.ConnectWifiAsync(endpoint.ToString(), ct);
        AdbDevice? device = await discovery.FindEndpointAsync(endpoint, ct);
        if (device is null)
        { await RefreshAsync(ct); SetStatus("Соединение Wi-Fi открыто. Выберите нужный телефон в списке."); return; }
        devices.Items.Clear(); devices.Items.Add(device); devices.SelectedIndex = 0;
        await ConnectDeviceAsync(device, ct, endpoint: endpoint.ToString());
    }
    private void ToggleCapture()
    {
        if (capture?.Active == true) { StopCapture(); return; }
        if (busy || disconnecting || closing || session is null || capture is null || !session.IsAlive) return;
        try
        {
            capture.Start(session, settings.Sensitivity, settings.PhoneSide, settings.EdgeReturnEnabled); edgeArmed = false; edgeSince = null;
            SetStatus(settings.EdgeReturnEnabled && session.AutoReturnReady
                ? "Управление телефоном. Для возврата двигайте курсор через край в сторону ноутбука."
                : "Управление телефоном. Автовозврат выключен или недоступен; Ctrl + Alt + F12 вернёт управление."); UpdateButtons();
        }
        catch (Exception e) { StopCapture(); SetStatus(e.Message, true); }
    }
    private void StopCapture()
    {
        bool active = capture?.Active == true;
        capture?.Stop(); edgeSince = null;
        if (active)
        { edgeArmed = false; if (!closing) SetStatus("Управление ноутбуком. Телефон остаётся подключён."); }
        if (!closing) UpdateButtons();
    }
    private void CheckEdge()
    {
        if (!edge.Checked || session is null || busy || disconnecting || closing || capture?.Active == true) { edgeSince = null; return; }
        Point point = Cursor.Position; Screen screen = Screen.FromPoint(point); Rectangle b = screen.Bounds;
        bool atEdge = EdgePolicy.AtEdge(new(b.X, b.Y, b.Width, b.Height), point.X, point.Y, settings.PhoneSide);
        if (!atEdge) { edgeArmed = true; edgeSince = null; return; }
        if (!edgeArmed || Native.MouseButtonDown || capture?.KeyHeld == true || Native.KeyDown(0x10) || Native.KeyDown(0x11) || Native.KeyDown(0x12)) { edgeSince = null; return; }
        Point outside = settings.PhoneSide switch
        {
            PhoneSide.Right => new(b.Right, point.Y), PhoneSide.Left => new(b.Left - 1, point.Y),
            PhoneSide.Top => new(point.X, b.Top - 1), _ => new(point.X, b.Bottom)
        };
        // Do not interrupt crossing to another Windows monitor.
        if (Screen.AllScreens.Any(s => s.DeviceName != screen.DeviceName && s.Bounds.Contains(outside))) { edgeSince = null; return; }
        edgeSince ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - edgeSince.Value).TotalMilliseconds >= 350) ToggleCapture();
    }
    private async Task PairAsync(CancellationToken ct)
    {
        recovery.Pause();
        using Form dialog = new() { Text = "Сопряжение с S25", Size = new Size(480, 330), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        TableLayoutPanel panel = new() { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16) };
        TextBox endpoint = new() { Dock = DockStyle.Fill, PlaceholderText = "IP:порт из окна кода сопряжения" };
        TextBox code = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 6 };
        Button ok = Button("Выполнить сопряжение"); ok.DialogResult = DialogResult.OK;
        Add(panel, Label("На S25 откройте «Сопряжение устройства с помощью кода». Этот порт отличается от порта подключения."));
        Add(panel, endpoint); Add(panel, Label("Шестизначный код")); Add(panel, code); Add(panel, ok);
        dialog.Controls.Add(panel); dialog.AcceptButton = ok;
        using CancellationTokenRegistration cancelDialog = CloseOnCancellation(dialog, ct);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetStatus("Выполняем сопряжение…");
        try { await adb.PairAsync(endpoint.Text, code.Text, ct); }
        finally { code.Clear(); }
        Log("Сопряжение выполнено. Код не сохранён.");
        await ConnectPairedAsync(AdbEndpoint.Parse(endpoint.Text), ct);
    }
    private static CancellationTokenRegistration CloseOnCancellation(Form dialog, CancellationToken ct)
    {
        dialog.Shown += (_, _) => { if (ct.IsCancellationRequested) dialog.Close(); };
        return ct.Register(() =>
        {
            if (!dialog.IsHandleCreated || dialog.IsDisposed) return;
            try { dialog.BeginInvoke(new Action(dialog.Close)); } catch (InvalidOperationException) { }
        });
    }
    private async Task PairQrAsync(CancellationToken ct)
    {
        recovery.Pause();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using QrPairingForm dialog = new(adb, ct, Log);
            DialogResult result;
            try { result = dialog.ShowDialog(this); }
            finally { await dialog.Completion; }
            ct.ThrowIfCancellationRequested();
            if (result == DialogResult.Retry) continue;
            if (result != DialogResult.OK || dialog.PairedEndpoint is not AdbEndpoint paired) return;
            Log("QR-сопряжение выполнено. Секрет не сохранён.");
            await ConnectPairedAsync(paired, ct); return;
        }
    }
    private async Task ConnectPairedAsync(AdbEndpoint pairing, CancellationToken ct)
    {
        SetStatus("Сопряжение выполнено. Ищем телефон для подключения…");
        (AdbDevice Device, string Endpoint)? found = await discovery.WaitPairedAsync(pairing, ct);
        if (found is null)
        { SetStatus("Сопряжение выполнено, но адрес подключения не обнаружен. Введите IP и порт с основной страницы «Беспроводная отладка»."); return; }
        wifi.Text = found.Value.Endpoint;
        devices.Items.Clear(); devices.Items.Add(found.Value.Device); devices.SelectedIndex = 0;
        await ConnectDeviceAsync(found.Value.Device, ct, endpoint: found.Value.Endpoint);
    }
    private async Task SendFileAsync(CancellationToken ct)
    {
        if (session is null) return;
        using OpenFileDialog dialog = new() { Title = "Отправить файлы на S25", Multiselect = true, CheckFileExists = true };
        ct.ThrowIfCancellationRequested();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ct.ThrowIfCancellationRequested();
        tabs.SelectedTab = filesPage;
        await SendFilesAsync(dialog.FileNames, ct);
    }

    private bool CanSendFiles => !busy && !disconnecting && !closing && !desktopLocked && !sleeping && session?.IsAlive == true;
    private void RegisterFileDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += FileDragEnter;
        control.DragOver += FileDragEnter;
        control.DragDrop += FileDragDrop;
        foreach (Control child in control.Controls) RegisterFileDrop(child);
    }
    private void FileDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;
        if (!CanSendFiles || (e.AllowedEffect & DragDropEffects.Copy) == 0) return;
        try
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && paths.All(File.Exists))
                e.Effect = DragDropEffects.Copy;
        }
        catch (Exception ex) when (ex is ExternalException or IOException) { }
    }
    private async void FileDragDrop(object? sender, DragEventArgs e)
    {
        if (!CanSendFiles || (e.AllowedEffect & DragDropEffects.Copy) == 0) { e.Effect = DragDropEffects.None; return; }
        try
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
            e.Effect = DragDropEffects.Copy;
            tabs.SelectedTab = filesPage;
            await RunAsync(ct => SendFilesAsync(paths, ct));
        }
        catch (Exception ex) when (ex is ExternalException or IOException)
        { SetStatus("Не удалось получить файлы из Проводника. Используйте «Выбрать файлы…».", true); }
    }
    private async Task SendFilesAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        PhoneSession current = session ?? throw new InvalidOperationException("Сначала подключите телефон.");
        if (!current.IsAlive) throw new IOException("Связь с телефоном потеряна.");
        string[] local = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (local.Length == 0) return;
        foreach (string path in local)
            if (!File.Exists(path)) throw new IOException("Отправка поддерживает только существующие файлы. Уберите папки и повторите выбор.");
        _ = FileTransfer.AllocateNames(local.Select(path => Path.GetFileName(path))); // Validate before any write.
        string directory = FileTransfer.Root;
        int completed = 0;
        transferProgress.Style = ProgressBarStyle.Marquee; transferProgress.Visible = true;
        transferStatus.Text = "Подготовка отправки…";
        try
        {
            ct.ThrowIfCancellationRequested();
            string[] existing = await adb.PrepareTransferDirectoryAsync(current.Serial, ct);
            string[] names = FileTransfer.AllocateNames(local.Select(path => Path.GetFileName(path)), existing);
            for (int i = 0; i < local.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (session != current || !current.IsAlive) throw new IOException("Связь с телефоном потеряна.");
                string temporaryName;
                do { temporaryName = ".gb-" + Guid.NewGuid().ToString("N") + ".part"; }
                while (names.Concat(existing).Contains(temporaryName, StringComparer.OrdinalIgnoreCase));
                transferStatus.Text = $"Передаём {i + 1} из {local.Length}: {names[i]}\nГотово: {completed}.";
                SetStatus($"Отправка файлов: {i + 1} из {local.Length}. Для остановки нажмите «Отменить».");
                await adb.PushFileAsync(current.Serial, local[i], directory + "/" + temporaryName, directory + "/" + names[i], ct);
                completed++;
            }
            transferStatus.Text = $"Отправлено: {completed} из {local.Length}.\nПапка на телефоне: {directory.Replace("/sdcard/", "", StringComparison.Ordinal)}";
            SetStatus($"Отправлено файлов: {completed}. Откройте Download/GalaxyBridge на телефоне.");
            Log($"Отправка завершена: {completed} файлов. Имена, пути и содержимое в журнал не записываются.");
        }
        catch (OperationCanceledException)
        {
            transferStatus.Text = $"Отправка остановлена. Подтверждено файлов: {completed} из {local.Length}.\nУже переданные файлы сохранены в {directory.Replace("/sdcard/", "", StringComparison.Ordinal)}.\nПри обрыве связи может остаться временный .part-файл.";
            Log($"Отправка остановлена: подтверждено {completed} из {local.Length} файлов.");
            throw;
        }
        catch (Exception)
        {
            transferStatus.Text = $"Отправка прервана. Подтверждено файлов: {completed} из {local.Length}.\nПроверьте папку {directory.Replace("/sdcard/", "", StringComparison.Ordinal)}.\nПосле восстановления связи повторите выбор оставшихся файлов.";
            Log($"Ошибка отправки: подтверждено {completed} из {local.Length} файлов.");
            throw;
        }
        finally { transferProgress.Visible = false; edgeArmed = false; edgeSince = null; }
    }
    private void OpenWindow() { StopCapture(); Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OpenInstructions()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "README.ru.md");
        if (!File.Exists(file)) { SetStatus("Инструкция находится в README.ru.md в архиве проекта."); return; }
        _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
    }
    private void SessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff)
            Ui(() => { desktopLocked = true; SuspendCapture(); if (automaticOperation) operation?.Cancel(); });
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
            Ui(() => { desktopLocked = false; recovery.Wake(Environment.TickCount64); });
    }
    private void PowerChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Ui(() => { sleeping = true; SuspendCapture(); if (automaticOperation) operation?.Cancel(); });
        else if (e.Mode == PowerModes.Resume) Ui(() => { sleeping = false; recovery.Wake(Environment.TickCount64); });
    }
    private void SuspendCapture() { StopCapture(); capture?.ResetPhysicalState(); }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closed) return;
        e.Cancel = true; if (closing) return;
        closing = true; StopCapture(); edgeTimer.Stop(); clipboardRetry.Stop(); autoTimer.Stop(); operation?.Cancel(); lifetime.Cancel();
        _ = Native.RemoveClipboardFormatListener(Handle);
        SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerChanged;
        tray.Visible = false;
        // Let an in-flight connection clean up its private forward/server before exiting.
        Task? pending = pendingOperation;
        if (pending is not null) { try { await pending; } catch (Exception) { } }
        if (pendingDisconnect is not null) { try { await pendingDisconnect; } catch (Exception) { } }
        await DisconnectAsync();
        capture?.Dispose();
        try { settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        tray.Dispose(); edgeTimer.Dispose(); clipboardRetry.Dispose(); autoTimer.Dispose(); lifetime.Dispose(); closed = true; Close();
    }
}
