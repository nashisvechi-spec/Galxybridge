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
    private ReceiptNotice? receiptNotice;
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
    private readonly Button toggle = new BridgeButton { Primary = true, Enabled = false, Text = "Управлять телефоном / DeX", AutoSize = true }, files = new BridgeButton { Primary = true, Enabled = false, Text = "Отправить файлы…", AutoSize = true };
    private readonly CheckBox edgeEntry = new() { Text = "Вход через край экрана", AutoSize = true };
    private readonly CheckBox showFileWidget = new() { Text = "Виджет отправки файлов на S25", AutoSize = true };
    private readonly CheckBox wheelZoom = new() { Text = "Масштабировать колёсиком по Wi-Fi", AutoSize = true };
    private readonly Label transferState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly Label receiveState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly CheckBox share = new() { Text = "Общий текстовый буфер", AutoSize = true };
    private readonly Label clipboardState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly Label bluetoothState = new() { AutoSize = true, MaximumSize = new Size(660, 0) };
    private readonly ComboBox bluetoothPeers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, Visible = false };
    private readonly Button bluetooth = new BridgeButton { Text = "Подключить Bluetooth (тест)", AutoSize = true };
    private readonly Button bluetoothStop = new BridgeButton { Text = "Отключить Bluetooth", AutoSize = true, Visible = false };
    private BleHidSession? nativeMouse;
    private Task? bluetoothOperation;
    private bool refreshingBluetooth, refreshBluetoothAgain;
    private sealed record BluetoothPeer(string Id, string Name) { public override string ToString() => Name; }
    private LanReceiveState received = new(false, 0, 0, "Файлы с телефона ещё не получены.");
    private readonly NumericUpDown speed = new() { Minimum = .25M, Maximum = 4M, Increment = .05M, DecimalPlaces = 2, Width = 90 };
    private readonly NumericUpDown transitionDelay = new() { Minimum = 0, Maximum = 2000, Increment = 50, Width = 90 };
    private FileDropForm? fileWindow;
    private FileTransferWidget? fileWidget;
    private int? sendPercent;
    private readonly ReceiveProgressWindow receiveProgress = new(() => new ShellReceiveProgress());
    private InputCapture? capture;
    private LanSession? session;
    private CancellationTokenSource? transfer;
    private Task? sending;
    private bool closing, finished, paused, suspended, armed;
    private readonly DisplayCaptureSwitch displaySwitch = new();
    private readonly PhoneWakeTransition wakeTransition = new();
    private bool desktopTarget;
    private long? edgeSince;
    private readonly Panel screenHost = new() { Dock = DockStyle.Fill };
    private readonly List<Panel> sections = [];
    private readonly List<BridgeButton> sectionButtons = [];
    private readonly DeviceMap deviceMap = new(), placementMap = new();
    private readonly Label deviceName = new() { AutoSize=true, Font=new Font("Segoe UI",16,FontStyle.Bold), Margin=new Padding(0,0,0,8) };
    private Form? pairingWindow;
    private BridgeCard? clipboardCard;
    private readonly List<string> transferHistory = [];
    private readonly Label recentTransfers = new() { Text="Завершённые передачи появятся здесь.",AutoSize=true,MaximumSize=new Size(650,0) };
    private void RecordTransfer(string direction,string name) { transferHistory.Insert(0,DateTime.Now.ToString("HH:mm")+" · "+direction+" · "+name+" · готово");if(transferHistory.Count>10) transferHistory.RemoveAt(10);recentTransfers.Text=string.Join("\n",transferHistory); }
    private readonly Label sidebarStatus = new() { AutoSize=true,MaximumSize=new Size(145,0),Margin=new Padding(0,20,0,0) };
    private void SelectSection(int index)
    {
        for(int i=0;i<sections.Count;i++) { sections[i].Visible=i==index;sectionButtons[i].Selected=i==index;sectionButtons[i].Invalidate(); }
        sections[index].BringToFront();
    }
    private void ApplyDesign()
    {
        BridgeTheme.Set(settings.ThemeMode);BridgeTheme.Apply(this);
        if(fileWindow is not null) BridgeTheme.Apply(fileWindow);
        if(pairingWindow is not null) BridgeTheme.Apply(pairingWindow);
        if(receiptNotice is not null) BridgeTheme.Apply(receiptNotice);
    }
    private void ThemeChanged(object? sender,UserPreferenceChangedEventArgs e) { if(settings.ThemeMode=="system") Ui(ApplyDesign); }

    public LanForm(Settings settings)
    {
        Icon = AppIcon.Value;
        this.settings = settings; host = new(settings);
        route = new(() => { capture?.Stop(); armed = false; edgeSince = null; });
        Text="Galaxy Bridge • 0.7.35";Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        AutoScaleDimensions=new SizeF(96,96);StartPosition=FormStartPosition.CenterScreen;
        Size=new Size(1000,Math.Min(740,(Screen.PrimaryScreen?.WorkingArea.Height??850)-40));MinimumSize=new Size(740,500);
        TableLayoutPanel shell=new() { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        FlowLayoutPanel sidebar=new() { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16,24,12,12),Tag="surface" };
        PictureBox brand=new() { Image=AppIcon.Value.ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Size=new Size(36,36),Margin=new Padding(0,0,0,10),Tag="brand" };
        sidebar.Controls.Add(brand);sidebar.Controls.Add(new Label { Text="Galaxy Bridge",AutoSize=true,Font=new Font("Segoe UI",13,FontStyle.Bold),Margin=new Padding(0,0,0,6) });
        sidebar.Controls.Add(BridgeTheme.Label("Ваш телефон.\nРядом."));
        string[] names=["Устройства","Управление","Файлы","Настройки"];
        for(int i=0;i<names.Length;i++) {
            int index=i;BridgeButton nav=BridgeTheme.Button(names[i]);nav.AutoSize=false;nav.Size=new Size(146,48);nav.AccessibleName=names[i];
            nav.Click+=(_,_)=>SelectSection(index);sidebar.Controls.Add(nav);sectionButtons.Add(nav);
            Panel panel=new() { Dock=DockStyle.Fill,Visible=i==0 };sections.Add(panel);screenHost.Controls.Add(panel);
        }
        sidebar.Controls.Add(BridgeTheme.Label("Версия 0.7.35\nWi-Fi · без отладки"));sidebar.Controls.Add(sidebarStatus);
        shell.Controls.Add(sidebar,0,0);shell.Controls.Add(screenHost,1,0);Controls.Add(shell);
        TableLayoutPanel devices=BridgeTheme.Page(sections[0],"Устройства","Один мост между телефоном и компьютером.");
        BridgeCard device=BridgeTheme.Card(devices,"Ваше подключение");device.Controls.Add(deviceName);device.Controls.Add(state);
        device.Controls.Add(deviceMap);device.Controls.Add(BridgeTheme.Row(toggle,BridgeTheme.Label("Ctrl + Alt + F12")));
        device.Controls.Add(connectionDetail);
        BridgeButton renew=BridgeTheme.Button("Подключить по QR");device.Controls.Add(renew);
        BridgeCard quick=BridgeTheme.Card(devices,"Всё под рукой");
        BridgeButton quickFiles=BridgeTheme.Button("Файлы в обе стороны"),quickText=BridgeTheme.Button("Текст и ссылки");
        quickFiles.Click+=(_,_)=> { SelectSection(2);if(sections[2].Controls[0] is Panel scroll) scroll.AutoScrollPosition=Point.Empty; };
        quickText.Click+=(_,_)=> { SelectSection(2);if(clipboardCard is not null && sections[2].Controls[0] is Panel scroll) scroll.ScrollControlIntoView(clipboardCard); };
        quick.Controls.Add(BridgeTheme.Row(quickFiles,quickText));quick.Controls.Add(BridgeTheme.Label("Отправка, приём и общий текстовый буфер."));
        TableLayoutPanel management=BridgeTheme.Page(sections[1],"Управление","Мышь и клавиатура — на нужном экране.");
        BridgeCard location=BridgeTheme.Card(management,"Расположение телефона");location.Controls.Add(placementMap);
        side.Items.AddRange(["Справа","Слева","Сверху","Снизу"]);side.SelectedIndex=(int)settings.PhoneSide;
        edgeEntry.Checked=settings.EdgeEntryEnabled;location.Controls.Add(BridgeTheme.Row(side,edgeEntry));
        location.Controls.Add(BridgeTheme.Label("Погасший экран телефона включается при переходе. Если установлен PIN или отпечаток, разблокируйте телефон обычным способом. DeX выбирается автоматически."));
        BridgeCard mouse=BridgeTheme.Card(management,"Курсор и переход");
        speed.Value=(decimal)settings.Sensitivity;TrackBar speedSlider=new() { Minimum=5,Maximum=80,Value=(int)(speed.Value/.05M),TickFrequency=15,Width=260,Height=40 };
        bool syncingSpeed=false;
        speedSlider.ValueChanged+=(_,_)=> { if(syncingSpeed) return;syncingSpeed=true;speed.Value=speedSlider.Value*.05M;syncingSpeed=false; };
        speed.ValueChanged+=(_,_)=> { if(syncingSpeed) return;syncingSpeed=true;speedSlider.Value=Math.Clamp((int)Math.Round(speed.Value/.05M),5,80);syncingSpeed=false; };
        BridgeButton normalSpeed=BridgeTheme.Button("Сбросить на 1×");normalSpeed.Click+=(_,_)=>speed.Value=1;
        mouse.Controls.Add(BridgeTheme.Label("Скорость курсора телефона / DeX · 0,25–4×"));mouse.Controls.Add(BridgeTheme.Row(speedSlider,speed,normalSpeed));
        transitionDelay.Value=settings.EdgeEntryDelayMs;BridgeButton normalDelay=BridgeTheme.Button("Сбросить на 350 мс");normalDelay.Click+=(_,_)=>transitionDelay.Value=350;
        mouse.Controls.Add(BridgeTheme.Label("Задержка перехода у края ПК, мс"));mouse.Controls.Add(BridgeTheme.Row(transitionDelay,normalDelay));
        wheelZoom.Checked=settings.WheelZoomEnabled;mouse.Controls.Add(wheelZoom);mouse.Controls.Add(BridgeTheme.Label("Ctrl + колёсико увеличивает PDF и изображения. Настройка выше включает масштабирование одним колёсиком."));
        BridgeCard navigation=BridgeTheme.Card(management,"Навигация Android");
        FlowLayoutPanel navigationButtons=BridgeTheme.Row();
        foreach(var(label,action) in new[]{("Назад","back"),("Домой","home"),("Недавние","recents")}) { BridgeButton b=BridgeTheme.Button(label);b.Click+=(_,_)=>session?.Post(new { type="nav",action });navigationButtons.Controls.Add(b); }
        navigation.Controls.Add(navigationButtons);navigation.Controls.Add(BridgeTheme.Label("Ctrl + Alt + F12 всегда возвращает управление на ПК."));
        TableLayoutPanel transfers=BridgeTheme.Page(sections[2],"Файлы","От компьютера к телефону — и обратно.");
        BridgeCard outgoing=BridgeTheme.Card(transfers,"ПК → телефон");outgoing.Controls.Add(BridgeTheme.Label("Перетащите файлы в окно или выберите их. На телефоне: Download/GalaxyBridge. До 2 ГБ на файл, без папок."));
        BridgeButton dropWindow=BridgeTheme.Button("Окно отправки и приёма");dropWindow.Click+=(_,_)=>OpenFiles();
        BridgeButton cancel=BridgeTheme.Button("Отменить отправку");cancel.Click+=(_,_)=>transfer?.Cancel();
        outgoing.Controls.Add(BridgeTheme.Row(files,dropWindow,cancel));outgoing.Controls.Add(transferState);
        showFileWidget.Checked=settings.FileWidgetVisible;outgoing.Controls.Add(showFileWidget);
        BridgeCard incoming=BridgeTheme.Card(transfers,"Телефон → ПК");incoming.Controls.Add(receiveState);
        incoming.Controls.Add(BridgeTheme.Label("На телефоне: Поделиться → Galaxy Bridge Wi-Fi. Отправка идёт в фоне. После сохранения на ПК появится уведомление."));
        BridgeButton receiveFolder=BridgeTheme.Button("Открыть папку приёма");receiveFolder.Click+=(_,_)=>OpenReceiveFolder();incoming.Controls.Add(receiveFolder);
        BridgeCard recent=BridgeTheme.Card(transfers,"Последние передачи");recent.Controls.Add(recentTransfers);recent.Controls.Add(BridgeTheme.Label("Последние 10 завершённых файлов в текущем сеансе."));
        BridgeCard text=BridgeTheme.Card(transfers,"Текст и ссылки");clipboardCard=text;share.Checked=settings.ClipboardEnabled;text.Controls.Add(share);
        clipboardState.Text="Копируйте на ПК и вставляйте на телефоне. Скопировав на телефоне, верните курсор на ПК и вставьте через Ctrl+V. Разрешите «Поверх других приложений».";text.Controls.Add(clipboardState);
        TableLayoutPanel preferences=BridgeTheme.Page(sections[3],"Настройки","Подключение, доступ и оформление.");
        BridgeCard appearance=BridgeTheme.Card(preferences,"Оформление");
        ComboBox theme=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=210 };theme.Items.AddRange(["Как в системе","Светлая","Тёмная"]);
        theme.SelectedIndex=settings.ThemeMode=="dark"?2:settings.ThemeMode=="light"?1:0;
        theme.SelectedIndexChanged+=(_,_)=> { settings.ThemeMode=theme.SelectedIndex==2?"dark":theme.SelectedIndex==1?"light":"system";SaveSettings();ApplyDesign(); };
        appearance.Controls.Add(theme);appearance.Controls.Add(BridgeTheme.Label("Синий — действие. Бирюзовый — связь."));
        BridgeCard widgetSettings=BridgeTheme.Card(preferences,"Виджет передачи файлов");
        widgetSettings.Controls.Add(BridgeTheme.Label("Прозрачность виджета"));
        TrackBar widgetTransparency=new() { Minimum=0,Maximum=80,Value=settings.FileWidgetTransparency,
            TickFrequency=20,SmallChange=1,LargeChange=10,Width=260,Height=40,AccessibleName="Прозрачность виджета, от 0 до 80 процентов" };
        Label widgetTransparencyValue=BridgeTheme.Label($"{settings.FileWidgetTransparency}%");
        widgetTransparency.ValueChanged+=(_,_)=> {
            settings.FileWidgetTransparency=widgetTransparency.Value;
            widgetTransparencyValue.Text=$"{settings.FileWidgetTransparency}%";
            fileWidget?.SetTransparency(settings.FileWidgetTransparency);SaveSettings();
        };
        widgetSettings.Controls.Add(BridgeTheme.Row(widgetTransparency,widgetTransparencyValue));
        widgetSettings.Controls.Add(BridgeTheme.Label("0% — обычная яркость, 80% — почти прозрачный. Изменения применяются сразу."));
        BridgeButton previewWidget=BridgeTheme.Button("Показать виджет");
        previewWidget.Click+=(_,_)=> { showFileWidget.Checked=true;OpenFileWidget(); };
        widgetSettings.Controls.Add(previewWidget);
        BridgeCard notices=BridgeTheme.Card(preferences,"Уведомления о файлах");
        notices.Controls.Add(BridgeTheme.Label("После сохранения файла появится карточка в углу экрана, даже если главное окно свёрнуто. Нажмите «Открыть папку», чтобы увидеть файл."));
        BridgeButton testNotice=BridgeTheme.Button("Проверить уведомление");
        testNotice.Click+=(_,_)=>ShowReceiptNotice("Проверка уведомлений","Galaxy Bridge · уведомления работают. Файл не создаётся.",settings.NativeReceiveFolder);
        notices.Controls.Add(testNotice);
        BridgeCard native=BridgeTheme.Card(preferences,"Системная мышь Bluetooth");native.Controls.Add(BridgeTheme.Row(bluetooth,bluetoothStop));native.Controls.Add(bluetoothPeers);native.Controls.Add(bluetoothState);
        bluetoothState.Text="Готовая BLE HID-связь включает системную мышь Android. Если адаптер её не поддерживает, доступно управление через Wi-Fi.";
        native.Controls.Add(BridgeTheme.Label("Для возврата через край подключите телефон по QR и разрешите «Поверх других приложений»."));
        BridgeCard connection=BridgeTheme.Card(preferences,"Подключение и доступ");
        BridgeButton pause=BridgeTheme.Button("Остановить / возобновить связь");pause.Click+=(_,_)=>ToggleConnection();
        BridgeButton forget=BridgeTheme.Button("Забыть телефон");forget.Click+=(_,_)=> { try { StopControl();host.Forget();ClearQr();UpdateState(); } catch(IOException) { state.Text="Не удалось сохранить изменение. Попробуйте ещё раз."; } };
        connection.Controls.Add(BridgeTheme.Row(pause,forget));connection.Controls.Add(BridgeTheme.Label("Управляет только сопряжённое устройство. Для мыши по Wi-Fi нужна служба Galaxy Bridge в специальных возможностях Android. При обрыве проверьте общую сеть, VPN и брандмауэр."));
        SelectSection(0);ApplyDesign();SystemEvents.UserPreferenceChanged+=ThemeChanged;
        share.CheckedChanged += (_, _) =>
        {
            settings.ClipboardEnabled = share.Checked; session?.ConfigureClipboard(share.Checked);
            clipboardRetry.Stop(); lastPushedSequence = Native.GetClipboardSequenceNumber();
            try { settings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { clipboardState.Text = "Настройка буфера применена, но не сохранена на диск."; }
        };
        bluetooth.Click += async (_, _) => await RunBluetoothAsync(true);
        bluetoothStop.Click += async (_, _) => await RunBluetoothAsync(false);
        bluetoothPeers.SelectedIndexChanged += (_, _) =>
        {
            StopControl();armed = false;
            if (bluetoothPeers.SelectedItem is BluetoothPeer peer) nativeMouse?.Select(peer.Id);
            UpdateState();
        };
        wheelZoom.CheckedChanged += (_, _) =>
        {
            StopControl(); armed = false; settings.WheelZoomEnabled = wheelZoom.Checked;
            if (session is not null) session.WheelZoomEnabled = wheelZoom.Checked;
            SaveSettings();
        };
        showFileWidget.CheckedChanged += (_, _) => {
            settings.FileWidgetVisible = showFileWidget.Checked;SaveSettings();
            if (showFileWidget.Checked) OpenFileWidget();else fileWidget?.Close();
        };
        renew.Click += (_, _) => { host.ClosePairing(); RefreshAddresses(); ShowQr(); }; addresses.SelectedIndexChanged += (_, _) => { if (host.PairingOpen) ShowQr(); };
        side.SelectedIndexChanged += (_, _) => { StopControl(); settings.PhoneSide = (PhoneSide)side.SelectedIndex; armed = false; SaveSettings(); };
        edgeEntry.CheckedChanged += (_, _) => { wakeTransition.Cancel(); settings.EdgeEntryEnabled = edgeEntry.Checked; SaveSettings(); };
        speed.ValueChanged += (_, _) =>
        {
            StopControl(); armed = false; settings.Sensitivity = (double)speed.Value;
            try { settings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { connectionDetail.Text = "Скорость применена, но не удалось сохранить настройку. При следующем запуске задайте её повторно."; }
        };
        transitionDelay.ValueChanged += (_, _) =>
        {
            settings.EdgeEntryDelayMs = (int)transitionDelay.Value;
            edgeSince = null; armed = false;
            SaveSettings();
        };
        toggle.Click += (_, _) => Toggle(); files.Click += async (_, _) => await ChooseFilesAsync();
        host.Connected += candidate => Ui(() => Attach(candidate));
        host.FileReceived += (phone, path) => Ui(() => NotifyFileReceived(phone, path));
        host.State += text => Ui(() => {
            if (!paused && !suspended && session?.IsAlive != true) connectionDetail.Text = text;
        });
        timer.Tick += (_, _) => { if (!host.PairingOpen) ClearQr(); UpdateState(); TryResumeWake(); TryResumeDisplayCapture(); CheckEdge(); };
        clipboardRetry.Tick += (_, _) => ReadLocalClipboard();
        SystemEvents.SessionSwitch += SessionChanged; SystemEvents.PowerModeChanged += PowerChanged;
        Shown += async (_, _) => { BridgeTheme.FitWindow(this);Start(); if (settings.FileWidgetVisible) OpenFileWidget();await RunBluetoothAsync(true); }; FormClosing += CloseAsync;
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
        paused = !paused; StopControl(); armed = false;
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
            capture.ToggleRequested += Toggle; capture.EdgeReturnRequested += () => StopControl();
            capture.DesktopChanged += () => Ui(StopControl); capture.Error += text => { state.Text = text; };
            host.Start(); RefreshAddresses(); timer.Start();
            connectionDetail.Text = "Разрешите Galaxy Bridge доступ в брандмауэре Windows для используемой сети. TCP 38271, UDP 38272.";
        }
        catch (Exception ex) when (ex is IOException or SocketException or System.ComponentModel.Win32Exception)
        { connectionDetail.Text = "Не удалось открыть режим Wi-Fi: " + ex.Message; }
    }
    private bool InputReady => route.Current is not null && session?.ScreenControlAllowed != false;
    private bool CanStartControl => InputReady || session?.CanWake == true;
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
        StopControl();armed = false;
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
            StopControl();
            if (addresses.Items.Count == 0) RefreshAddresses();
            if (addresses.SelectedItem is not IPAddress address) { state.Text = "Подключите ноутбук к Wi-Fi и повторите."; return; }
            paused = false;
            using QRCodeGenerator generator = new(); using QRCodeData data = generator.CreateQrCode(host.NewQr(address), QRCodeGenerator.ECCLevel.M);
            using QRCode code = new(data); Image image = code.GetGraphic(6); Image? old = qr.Image; qr.Image = image; qr.Visible = true;ShowPairingWindow(); old?.Dispose();
            state.Text = "Сканируйте в приложении Galaxy Bridge Wi-Fi. Код действует 2 минуты.";
            connectionDetail.Text = "Ожидаем соединение на " + address + ":" + LanProtocol.Port + ". После сканирования нажмите «Подключить» на S25. Если этот статус не меняется, проверьте адрес, общую сеть и брандмауэр Windows.";
        }
        catch (Exception ex) when (ex is NetworkInformationException or SocketException) { state.Text = "Не удалось определить адрес сети."; }
    }
    private void ShowPairingWindow()
    {
        if(pairingWindow is null) {
            Form window=new() { Text="Подключить телефон · Galaxy Bridge",Icon=AppIcon.Value,Font=Font,Size=new Size(500,600),MinimumSize=new Size(400,340),StartPosition=FormStartPosition.CenterParent,AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new SizeF(96,96) };
            TableLayoutPanel content=new() { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.AutoSize));content.RowStyles.Add(new RowStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label help=BridgeTheme.Label("Сканируйте QR в Galaxy Bridge на телефоне. Оба устройства должны быть в одной локальной сети. Код действует 2 минуты.");help.Dock=DockStyle.Fill;content.Controls.Add(help,0,0);
            qr.Dock=DockStyle.Fill;content.Controls.Add(qr,0,1);content.Controls.Add(addresses,0,2);
            window.Controls.Add(content);window.FormClosing+=(_,e)=> { if(!closing) { e.Cancel=true;host.ClosePairing();ClearQr(); } };
            window.Shown+=(_,_)=>BridgeTheme.FitWindow(window);pairingWindow=window;
        }
        BridgeTheme.Apply(pairingWindow);if(!pairingWindow.Visible) pairingWindow.Show(this);else pairingWindow.Activate();
    }
    private void ClearQr() { pairingWindow?.Hide();if(qr.IsDisposed) return;Image? image = qr.Image; qr.Image = null; qr.Visible = false; image?.Dispose(); }
    private void Attach(LanSession candidate)
    {
        if (!host.IsCurrent(candidate) || paused || suspended) return;
        connectionDetail.Text = "Связь с телефоном установлена. Файлы и управление доступны.";
        candidate.WheelZoomEnabled = settings.WheelZoomEnabled;
        StopControl(); session = candidate; armed = false; edgeSince = null; ClearQr(); host.ClosePairing();
        candidate.ClipboardWriter = (text, ct) => SetPhoneClipboardAsync(candidate, text, ct);
        candidate.ClipboardSequenceReader = Native.GetClipboardSequenceNumber;
        candidate.ClipboardReadAllowed = () => !closing && !paused && !suspended && candidate == session;
        candidate.AutomaticClipboardWriter = (text, sequence, ct) => SetPhoneClipboardAsync(candidate, text, ct, sequence);
        candidate.ClipboardStatus += text => Ui(() => { if (session == candidate) clipboardState.Text = text; });
        candidate.NativeEdgeStatus += text => Ui(() => { if (session == candidate) bluetoothState.Text = text; });
        candidate.WakeAcknowledged += accepted => Ui(() => {
            if(session==candidate && wakeTransition.Pending && !accepted) {
                wakeTransition.Cancel();connectionDetail.Text="Не удалось включить экран. Проверьте специальные возможности или разрешение «Поверх других приложений» на телефоне.";
            }
        });
        candidate.ControlDisplayChanged += moved => Ui(() => {
            if(session==candidate) {
                if(moved) wakeTransition.Cancel();
                displaySwitch.Changed(capture?.Active==true,moved,Environment.TickCount64);
                capture?.Stop();armed=false;edgeSince=null;UpdateState();TryResumeDisplayCapture();
            }
        });
        lastPushedSequence = Native.GetClipboardSequenceNumber(); clipboardRetry.Stop();
        candidate.ConfigureClipboard(share.Checked);
        if (!candidate.AutomaticClipboard) clipboardState.Text = "Для автоматического общего буфера обновите Galaxy Bridge Wi-Fi на телефоне до 0.7.13.";
        candidate.Lost += () => Ui(() => { if (session == candidate) {
            received = candidate.ReceiveState; StopControl(); session = null; armed = false;
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
        deviceName.Text=session?.IsAlive==true?session.Name:"Ваш телефон";
        sidebarStatus.Text=session?.IsAlive==true?"Подключено\n"+session.Name:nativeMouse?.InputReady==true?"Bluetooth готов":"Ожидаем телефон";
        sidebarStatus.ForeColor=session?.IsAlive==true?BridgeTheme.Success:BridgeTheme.Muted;
        state.ForeColor=session?.IsAlive==true?BridgeTheme.Success:BridgeTheme.Muted;
        foreach(DeviceMap map in new[]{deviceMap,placementMap}) {
            bool connected=session?.IsAlive==true || nativeMouse?.InputReady==true,desktopMap=session?.PreferWifiInput==true;
            if(map.Side!=settings.PhoneSide || map.Connected!=connected || map.Desktop!=desktopMap) {
                map.Side=settings.PhoneSide;map.Height=(int)Math.Round((settings.PhoneSide is PhoneSide.Top or PhoneSide.Bottom?260:190)*map.DeviceDpi/96f);map.Connected=connected;map.Desktop=desktopMap;map.Invalidate();
            }
        }
        bool desktop=session?.PreferWifiInput==true;
        if(desktop!=desktopTarget) displaySwitch.Changed(capture?.Active==true,true,Environment.TickCount64);
        desktopTarget=desktop;
        route.Update(nativeMouse, session, session?.PreferWifiInput==true);
        toggle.Enabled = !closing && !suspended && !paused && sending is null && (CanStartControl || wakeTransition.Pending) && capture is not null;
        files.Enabled = !closing && !suspended && !paused && sending is null && session?.IsAlive == true;
        UpdateFiles();
        toggle.Text = wakeTransition.Pending ? "Отменить переход" : capture?.Active == true ? "Вернуть курсор на ПК" : "Управлять телефоном / DeX";
        if (qr.Image is not null) return;
        if (route.Current is BleHidSession)
        {
            state.Text = paused ? "Подключение остановлено." : "Мышь: системная, по Bluetooth. " +
                (session?.IsAlive == true ? "Файлы и общий буфер подключены через Wi-Fi." : "Для файлов и общего буфера подключите приложение телефона по QR.");
            return;
        }
        state.Text = paused ? "Подключение остановлено." : session?.IsAlive == true ? "Подключён: " + session.Name +
            (session.InputReady ? session.PreferWifiInput ? ". Экран DeX: курсор приложения, по Wi-Fi." : ". Мышь: курсор приложения, по Wi-Fi." : ". Для управления проверьте выбранный экран и специальные возможности.") : "Ожидаем телефон по Wi-Fi или Bluetooth.";
    }
    private void StopControl() { wakeTransition.Cancel();displaySwitch.Cancel();capture?.Stop(); }
    private bool AtPhoneEdge()
    {
        Point p=Cursor.Position;Screen screen=Screen.FromPoint(p);Rectangle b=screen.Bounds;
        bool at=settings.PhoneSide switch { PhoneSide.Right => p.X>=b.Right-1,PhoneSide.Left => p.X<=b.Left,
            PhoneSide.Top => p.Y<=b.Top,PhoneSide.Bottom => p.Y>=b.Bottom-1,_ => false };
        Point outside=settings.PhoneSide switch { PhoneSide.Right => new(b.Right,p.Y),PhoneSide.Left => new(b.Left-1,p.Y),
            PhoneSide.Top => new(p.X,b.Top-1),_ => new(p.X,b.Bottom) };
        return at && !Screen.AllScreens.Any(s => s.DeviceName!=screen.DeviceName && s.Bounds.Contains(outside));
    }
    private void TryResumeWake()
    {
        if(!wakeTransition.Pending) return;
        bool allowed=!closing && !suspended && !paused && sending is null && session?.IsAlive==true && session.PhoneDisplaySelected && capture is not null;
        bool held=Native.MouseButtonDown || capture?.MouseHeld==true || capture?.KeyHeld==true || Native.KeyDown(0x10) || Native.KeyDown(0x11) || Native.KeyDown(0x12);
        if(!wakeTransition.ShouldResume(allowed,InputReady,held,edgeEntry.Checked && AtPhoneEdge(),Environment.TickCount64)) {
            if(!wakeTransition.Pending) connectionDetail.Text="Переход отменён. Если телефон заблокирован, разблокируйте его и снова подведите курсор к краю.";
            return;
        }
        wakeTransition.Cancel();Toggle();
    }
    private void TryResumeDisplayCapture()
    {
        bool allowed=!closing && !suspended && !paused && sending is null && session?.IsAlive==true && capture is not null;
        if(!displaySwitch.ShouldResume(allowed,InputReady,Native.MouseButtonDown || capture?.MouseHeld==true || capture?.KeyHeld==true,Environment.TickCount64)) return;
        if(capture!.Active) { displaySwitch.Cancel();return; }
        try {
            if(nativeMouse is not null) nativeMouse.ClipboardSession=session;
            capture.Start(route.Current!,settings.Sensitivity,settings.PhoneSide,true);
            displaySwitch.Cancel();armed=false;
        } catch(InvalidOperationException ex) { connectionDetail.Text=ex.Message; }
    }
    private void Toggle() => StartControl(false);
    private void StartControl(bool fromEdge)
    {
        displaySwitch.Cancel();
        if(wakeTransition.Pending) { wakeTransition.Cancel();armed=false;return; }
        route.Update(nativeMouse, session, session?.PreferWifiInput==true);
        if (capture?.Active == true) { StopControl(); armed = false; return; }
        if (closing || suspended || paused || sending is not null || capture is null) return;
        if(session?.CanWake==true) {
            if(session.RequestWake()) {
                wakeTransition.Begin(fromEdge,Environment.TickCount64);armed=false;edgeSince=null;
                connectionDetail.Text="Включаем экран телефона. Если он заблокирован, разблокируйте его; курсор пока остаётся на ПК.";
            }
            return;
        }
        if(!InputReady) return;
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
        if (!edgeEntry.Checked || !toggle.Enabled || capture?.Active == true || wakeTransition.Pending) { edgeSince = null; return; }
        Point p = Cursor.Position; Screen screen = Screen.FromPoint(p); Rectangle b = screen.Bounds;
        bool at = settings.PhoneSide switch { PhoneSide.Right => p.X >= b.Right - 1, PhoneSide.Left => p.X <= b.Left,
            PhoneSide.Top => p.Y <= b.Top, PhoneSide.Bottom => p.Y >= b.Bottom - 1, _ => false };
        if (!at) { armed = true; edgeSince = null; return; }
        if (!armed || Native.MouseButtonDown || capture?.MouseHeld == true || capture?.KeyHeld == true || Native.KeyDown(0x10) || Native.KeyDown(0x11) || Native.KeyDown(0x12)) { edgeSince = null; return; }
        Point outside = settings.PhoneSide switch { PhoneSide.Right => new(b.Right, p.Y), PhoneSide.Left => new(b.Left - 1, p.Y),
            PhoneSide.Top => new(p.X, b.Top - 1), _ => new(p.X, b.Bottom) };
        if (Screen.AllScreens.Any(s => s.DeviceName != screen.DeviceName && s.Bounds.Contains(outside))) { edgeSince = null; return; }
        edgeSince ??= Environment.TickCount64;
        if (Environment.TickCount64 - edgeSince.Value >= settings.EdgeEntryDelayMs) StartControl(true);
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
        StopControl(); armed = false;
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
        widget.SetTransparency(settings.FileWidgetTransparency);
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
        LanSession? current = !closing && !paused && !suspended && session?.IsAlive == true ? session : null;
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
    { OpenReceiveFolder(settings.NativeReceiveFolder); }
    private void NotifyFileReceived(string phone, string path)
    {
        RecordTransfer("Телефон → ПК",Path.GetFileName(path));
        string folder=Path.GetDirectoryName(path)??settings.NativeReceiveFolder;
        ShowReceiptNotice("Файл получен с телефона",Path.GetFileName(path)+"\nОт: "+phone,folder);
    }
    private void ShowReceiptNotice(string title,string text,string folder)
    {
        try
        {
            if(receiptNotice is null || receiptNotice.IsDisposed) receiptNotice=new ReceiptNotice();
            receiptNotice.Present(title,text,Screen.FromControl(this).WorkingArea,()=>OpenReceiveFolder(folder));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        { connectionDetail.Text = title+": "+text; }
    }
    private void OpenReceiveFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
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
        StopControl(); armed = false;
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
                    await current.SendFileAsync(path, cancel.Token, progress);done++;RecordTransfer("ПК → телефон",Path.GetFileName(path));
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
    private void Suspend() { suspended = true; armed = false; StopControl(); transfer?.Cancel(); host.Pause(); ClearQr(); }
    private void Resume() { suspended = false; capture?.ResetPhysicalState(); if (!paused) host.Resume(); armed = false; }
    private async void CloseAsync(object? sender, FormClosingEventArgs e)
    {
        if (finished) return; e.Cancel = true; if (closing) return; closing = true;
        timer.Stop(); clipboardRetry.Stop(); receiveProgress.Dispose(); StopControl(); transfer?.Cancel(); lifetime.Cancel();
        UpdateState(); fileWindow?.Close(); fileWindow = null;fileWidget?.Close();fileWidget = null;
        ClearQr();pairingWindow?.Close();pairingWindow=null;
        receiptNotice?.Close();receiptNotice=null;
        SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;SystemEvents.UserPreferenceChanged-=ThemeChanged;
        if (sending is not null) await sending;
        if (bluetoothOperation is not null) await bluetoothOperation;
        if (nativeMouse is not null) { await nativeMouse.DisposeAsync();nativeMouse = null; }
        await host.DisposeAsync(); capture?.Dispose(); ClearQr(); timer.Dispose(); clipboardRetry.Dispose(); lifetime.Dispose();
        SaveSettings(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
        finished = true; Close();
    }
}
