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
    private InputCapture? capture;
    private PhoneSession? session;
    private CancellationTokenSource? operation;
    private Task? pendingOperation, pendingDisconnect;
    private bool busy, disconnecting, closing, closed, edgeArmed = true;
    private string? cachedClipboard;
    private DateTime? edgeSince;
    private readonly ComboBox devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown speed = new() { Minimum = .25M, Maximum = 4M, Increment = .25M, DecimalPlaces = 2, Dock = DockStyle.Fill };
    private readonly CheckBox share = new() { Text = "Общий текстовый буфер", AutoSize = true };
    private readonly CheckBox edge = new() { Text = "Вход на телефон через край экрана", AutoSize = true };
    private readonly TextBox wifi = new() { PlaceholderText = "192.168.1.10:37121", Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(660, 0), Padding = new Padding(0, 12, 0, 12) };
    private readonly TextBox journal = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button refresh = Button("Найти устройства"), connect = Button("Подключить выбранный"),
        disconnect = Button("Отключить"), toggle = Button("Управлять телефоном"), pair = Button("Сопряжение Wi-Fi"),
        wifiConnect = Button("Подключиться по Wi-Fi"), sendFile = Button("Отправить файл"), layout = Button("Раскладка клавиатуры");
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Visible = true, Text = "Galaxy Bridge" };
    private readonly System.Windows.Forms.Timer edgeTimer = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer clipboardRetry = new() { Interval = 50 };
    private int clipboardRetries;

    public MainForm()
    {
        Text = "Galaxy Bridge • Windows 10"; StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, Math.Min(760, (Screen.PrimaryScreen?.WorkingArea.Height ?? 800) - 50));
        MinimumSize = new Size(640, 480); BackColor = Color.FromArgb(18, 24, 38);
        ForeColor = Color.FromArgb(232, 237, 247); Font = new Font("Segoe UI", 10);
        BuildUi();
        side.Items.AddRange(["Справа", "Слева", "Сверху", "Снизу"]);
        side.SelectedIndex = (int)settings.PhoneSide;
        speed.Value = (decimal)settings.Sensitivity; share.Checked = settings.ClipboardEnabled; edge.Checked = settings.EdgeEntryEnabled;
        refresh.Click += async (_, _) => await RunAsync(RefreshAsync);
        connect.Click += async (_, _) => await RunAsync(ConnectAsync);
        pair.Click += async (_, _) => await RunAsync(PairAsync);
        wifiConnect.Click += async (_, _) => await RunAsync(async ct => { await adb.ConnectWifiAsync(wifi.Text, ct); await RefreshAsync(ct); SetStatus("Телефон найден по Wi-Fi. Выберите его и нажмите «Подключить выбранный»."); });
        disconnect.Click += async (_, _) => { if (busy) operation?.Cancel(); else await DisconnectAsync(); };
        toggle.Click += (_, _) => ToggleCapture();
        sendFile.Click += async (_, _) => await RunAsync(SendFileAsync);
        layout.Click += (_, _) => session?.Send([15]);
        side.SelectedIndexChanged += (_, _) => { settings.PhoneSide = (PhoneSide)side.SelectedIndex; edgeSince = null; };
        speed.ValueChanged += (_, _) => { settings.Sensitivity = (double)speed.Value; if (capture?.Active == true) StopCapture(); };
        share.CheckedChanged += (_, _) => settings.ClipboardEnabled = share.Checked;
        edge.CheckedChanged += (_, _) => { settings.EdgeEntryEnabled = edge.Checked; edgeSince = null; };
        edgeTimer.Tick += (_, _) => CheckEdge();
        clipboardRetry.Tick += (_, _) => { if (++clipboardRetries > 3) clipboardRetry.Stop(); else ReadClipboard(); };
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
        TabControl tabs = new() { Dock = DockStyle.Fill, ForeColor = SystemColors.ControlText };
        TableLayoutPanel connection = Page(), controls = Page(), help = Page();
        TabPage connectPage = new("Подключение"), controlPage = new("Управление"), helpPage = new("Помощь и журнал");
        connectPage.Controls.Add(connection); controlPage.Controls.Add(controls); helpPage.Controls.Add(help);
        tabs.TabPages.AddRange([connectPage, controlPage, helpPage]); root.Controls.Add(tabs, 0, 2);
        Add(connection, Label("Телефон через USB или Wi-Fi")); Add(connection, devices); Add(connection, Row(refresh, connect));
        Add(connection, Label("USB: подключите S25 кабелем для передачи данных и разрешите отладку на телефоне."));
        Add(connection, Label("Wi-Fi: включите «Беспроводная отладка». Для первого подключения используйте код сопряжения."));
        Add(connection, pair); Add(connection, Label("IP-адрес и порт подключения с основной страницы «Беспроводная отладка»:"));
        Add(connection, wifi); Add(connection, wifiConnect);
        Add(controls, Label("Расположение телефона относительно монитора")); Add(controls, side);
        Add(controls, edge); Add(controls, Label("Задержите курсор у выбранного края на 350 мс. Возврат на ноутбук — Ctrl + Alt + F12. Автовход по умолчанию выключен."));
        Add(controls, Label("Скорость мыши")); Add(controls, speed); Add(controls, share);
        Add(controls, Label("Ctrl + C / Ctrl + X на телефоне передают текст ноутбуку. Ctrl + V вставляет текст с ноутбука на телефон."));
        Add(controls, Row(layout, sendFile));
        Add(controls, Label("Русский ввод: один раз выберите русскую и английскую раскладки для Galaxy Bridge Keyboard в настройках телефона."));
        Add(help, Label("Ctrl + Alt + F12 — переключение управления. На S25 при необходимости: Ctrl + Alt + Fn + F12 на ноутбуке."));
        Add(help, Label("Экран телефона должен быть разблокирован. Изображение остаётся на S25. Файлы сохраняются в папку Download. Буфер поддерживает текст. Удерживать экран включённым программа не заставляет."));
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
            capture.Error += text => Ui(() => { StopCapture(); SetStatus(text, true); });
            _ = Native.AddClipboardFormatListener(Handle); ReadClipboard(); edgeTimer.Start();
            await RunAsync(RefreshAsync);
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
        refresh.Enabled = connect.Enabled = pair.Enabled = wifiConnect.Enabled = !busy && !attached && available;
        devices.Enabled = !busy && !attached && available;
        toggle.Enabled = !busy && attached && capture is not null && available;
        sendFile.Enabled = layout.Enabled = !busy && attached && available;
        disconnect.Enabled = (busy || attached) && available; disconnect.Text = busy ? "Отменить" : "Отключить";
        toggle.Text = capture?.Active == true ? "Вернуть управление ноутбуку" : "Управлять телефоном";
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || disconnecting || closing) return;
        StopCapture(); busy = true;
        using CancellationTokenSource current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = current; UpdateButtons();
        try { pendingOperation = action(current.Token); await pendingOperation; }
        catch (OperationCanceledException) { if (!closing) SetStatus("Операция отменена."); }
        catch (Exception e) { if (!closing) { SetStatus(e.Message, true); Log("Операция не выполнена: " + e.GetType().Name); } }
        finally { pendingOperation = null; operation = null; busy = false; if (!closing) UpdateButtons(); }
    }
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
        SetStatus("Подключаемся к телефону…");
        PhoneSession candidate = new(adb, device.Serial, Log);
        candidate.ClipboardReceived += text => SetPhoneClipboard(candidate, text);
        candidate.ConnectionLost += text => Ui(async () =>
        {
            if (session == candidate) { StopCapture(); await DisconnectAsync(); SetStatus(text, true); }
        });
        try
        {
            await candidate.ConnectAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (!candidate.IsAlive) throw new IOException("Соединение завершилось при подключении.");
            session = candidate;
            SetStatus($"{candidate.DeviceName} подключён. Ctrl + Alt + F12 передаст ему управление.");
            Log("Подключено устройство. Видео и звук не передаются.");
        }
        catch { await candidate.DisposeAsync(); throw; }
    }
    private async Task DisconnectAsync()
    {
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
    private void ToggleCapture()
    {
        if (capture?.Active == true) { StopCapture(); return; }
        if (busy || disconnecting || closing || session is null || capture is null || !session.IsAlive) return;
        try
        {
            capture.Start(session, settings.Sensitivity); edgeArmed = false; edgeSince = null;
            SetStatus("Управление телефоном. Ctrl + Alt + F12 вернёт мышь и клавиатуру ноутбуку."); UpdateButtons();
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
        using Form dialog = new() { Text = "Сопряжение с S25", Size = new Size(480, 330), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        TableLayoutPanel panel = new() { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16) };
        TextBox endpoint = new() { Dock = DockStyle.Fill, PlaceholderText = "IP:порт из окна кода сопряжения" };
        TextBox code = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 6 };
        Button ok = Button("Выполнить сопряжение"); ok.DialogResult = DialogResult.OK;
        Add(panel, Label("На S25 откройте «Сопряжение устройства с помощью кода». Этот порт отличается от порта подключения."));
        Add(panel, endpoint); Add(panel, Label("Шестизначный код")); Add(panel, code); Add(panel, ok);
        dialog.Controls.Add(panel); dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetStatus("Выполняем сопряжение…");
        try { await adb.PairAsync(endpoint.Text, code.Text, ct); }
        finally { code.Clear(); }
        SetStatus("Сопряжение выполнено. Введите порт с основной страницы «Беспроводная отладка» и подключитесь по Wi-Fi.");
        Log("Сопряжение выполнено. Код не сохранён.");
    }
    private async Task SendFileAsync(CancellationToken ct)
    {
        PhoneSession? current = session;
        if (current is null) return;
        using OpenFileDialog dialog = new() { Title = "Отправить файл в Download на S25", Multiselect = false, CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetStatus("Передаём файл в Download…");
        _ = await adb.PushAsync(current.Serial, dialog.FileName, ct);
        SetStatus("Файл отправлен в Download на телефоне."); Log("Файл отправлен. Содержимое файла не записывалось в журнал.");
    }
    private void OpenWindow() { StopCapture(); Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OpenInstructions()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "README.ru.md");
        if (!File.Exists(file)) { SetStatus("Инструкция находится в README.ru.md в архиве проекта."); return; }
        _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
    }
    private void SessionSwitch(object? sender, SessionSwitchEventArgs e)
    { if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff) Ui(SuspendCapture); }
    private void PowerChanged(object? sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Suspend) Ui(SuspendCapture); }
    private void SuspendCapture() { StopCapture(); capture?.ResetPhysicalState(); }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closed) return;
        e.Cancel = true; if (closing) return;
        closing = true; StopCapture(); edgeTimer.Stop(); clipboardRetry.Stop(); operation?.Cancel(); lifetime.Cancel();
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
        tray.Dispose(); edgeTimer.Dispose(); clipboardRetry.Dispose(); lifetime.Dispose(); closed = true; Close();
    }
}
