using System.Security.Cryptography;
using GalaxyBridge.Core;
using QRCoder;

namespace GalaxyBridge.Windows;

internal sealed class QrPairingForm : Form
{
    private readonly AdbClient adb;
    private readonly Action<string> log;
    private readonly CancellationTokenSource lifetime;
    private readonly Label state = new() { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 12, 0, 12) };
    private readonly QrView qr;
    private readonly CancellationTokenRegistration externalCancel;
    private AdbQrPairing? credentials;
    private bool cancelledByUser;
    public AdbEndpoint? PairedEndpoint { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;

    public QrPairingForm(AdbClient adb, CancellationToken ct, Action<string> log)
    {
        this.adb = adb; this.log = log;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2));
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        string name = "studio-" + new string(Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        credentials = new(name, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        qr = new QrView(credentials.Payload) { Size = new Size(320, 320), BackColor = Color.White, Margin = new Padding(0, 12, 0, 0) };
        Text = "Сопряжение по QR-коду"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(520, Math.Min(660, (Screen.PrimaryScreen?.WorkingArea.Height ?? 800) - 50));
        MinimumSize = new Size(400, 400); MinimizeBox = MaximizeBox = false;
        Font = new Font("Segoe UI", 10);
        FlowLayoutPanel panel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        panel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(440, 0), Text =
            "Подключите телефон и ноутбук к одной Wi-Fi-сети. На S25: Параметры разработчика → Беспроводная отладка → Сопряжение устройства с помощью QR-кода. Сканируйте именно этим сканером, а не обычной камерой. Код действует 2 минуты." });
        panel.Controls.Add(qr); state.Text = "Ожидаем сканирования на телефоне…"; panel.Controls.Add(state);
        Button cancel = new() { Text = "Закрыть", AutoSize = true, MinimumSize = new Size(120, 36) };
        Button renew = new() { Text = "Новый QR-код", AutoSize = true, MinimumSize = new Size(120, 36) };
        renew.Click += (_, _) => { cancelledByUser = true; DialogResult = DialogResult.Retry; Close(); };
        cancel.Click += (_, _) => { cancelledByUser = true; Close(); };
        FlowLayoutPanel buttons = new() { AutoSize = true }; buttons.Controls.Add(renew); buttons.Controls.Add(cancel);
        panel.Controls.Add(buttons); CancelButton = cancel;
        Controls.Add(panel);
        Shown += (_, _) => { if (ct.IsCancellationRequested) Close(); else Completion = PairAsync(); };
        FormClosing += (_, _) => { cancelledByUser = true; lifetime.Cancel(); credentials = null; qr.Clear(); };
        externalCancel = ct.Register(() =>
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(new Action(() => { cancelledByUser = true; Close(); })); } catch (InvalidOperationException) { }
        });
    }

    private async Task PairAsync()
    {
        try
        {
            adb.VerifyBackend();
            AdbQrPairing active = credentials ?? throw new OperationCanceledException();
            bool reportedAdbFailure = false, reportedLocalFailure = false, reportedPairFailure = false;
            long retryAt = 0, started = Environment.TickCount64;
            while (true)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                IReadOnlyList<AdbMdnsService> services = [];
                try { services = await adb.MdnsAsync(lifetime.Token); }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    if (!reportedAdbFailure) { log("QR: поиск через ADB mDNS недоступен; включён прямой поиск в локальной сети."); reportedAdbFailure = true; }
                }
                AdbMdnsService? service = services.FirstOrDefault(active.Matches);
                if (service is null)
                {
                    try { service = (await LocalMdnsDiscovery.PairingAsync(active, lifetime.Token)).FirstOrDefault(active.Matches); }
                    catch (System.Net.NetworkInformation.NetworkInformationException)
                    { if (!reportedLocalFailure) { log("QR: не удалось прочитать сетевые интерфейсы Windows."); reportedLocalFailure = true; } }
                }
                if (service is not null)
                {
                    if (Environment.TickCount64 >= retryAt)
                    {
                        state.Text = "Телефон найден. Выполняем сопряжение…";
                        try
                        {
                            await adb.PairQrAsync(service.Endpoint, active, lifetime.Token);
                            lifetime.Token.ThrowIfCancellationRequested();
                            log("QR: сопряжение подтверждено ADB.");
                            PairedEndpoint = service.Endpoint; DialogResult = DialogResult.OK; Close(); return;
                        }
                        catch (Exception ex) when (ex is IOException or TimeoutException)
                        {
                            retryAt = Environment.TickCount64 + 3000;
                            state.Text = "Телефон найден, но ADB не подтвердил сопряжение. Повторяем попытку. Если сканер на S25 сообщил об ошибке, нажмите «Новый QR-код» и сканируйте заново.";
                            if (!reportedPairFailure) { log("QR: устройство обнаружено; сопряжение не подтверждено. Выполняются повторные попытки."); reportedPairFailure = true; }
                        }
                    }
                }
                else if (Environment.TickCount64 - started > 8000)
                {
                    state.Text = "Служба сканировавшего телефона пока не найдена. Поиск продолжается через ADB и напрямую. Откройте QR-сканер в «Беспроводной отладке». Если на S25 написано «Сопряжение…», проверьте общую сеть, брандмауэр и изоляцию устройств в роутере.";
                }
                await Task.Delay(1000, lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !Disposing && !cancelledByUser)
            { qr.Clear(); state.Text = "Код истёк. Нажмите «Новый QR-код» и повторите сканирование. Если телефон не обнаружен, используйте сопряжение кодом."; log("QR: двухминутное ожидание завершено."); }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed && !Disposing)
            { qr.Clear(); state.Text = "Не удалось запустить компонент подключения. Проверьте наличие полной папки backend рядом с GalaxyBridge.exe. Можно закрыть окно и повторить подключение кодом."; log("QR: компонент подключения недоступен (" + ex.GetType().Name + ")."); }
        }
        finally { credentials = null; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { externalCancel.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed class QrView : Control
    {
        private QRCodeData? data;
        public QrView(string payload)
        {
            using QRCodeGenerator generator = new();
            data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            DoubleBuffered = true;
        }
        public void Clear() { data?.Dispose(); data = null; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.Clear(Color.White);
            if (data is null) return;
            int n = data.ModuleMatrix.Count, pixels = Math.Min(ClientSize.Width, ClientSize.Height) / n;
            if (pixels < 1) return;
            int left = (ClientSize.Width - n * pixels) / 2, top = (ClientSize.Height - n * pixels) / 2;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                if (data.ModuleMatrix[y][x]) e.Graphics.FillRectangle(Brushes.Black, left + x * pixels, top + y * pixels, pixels, pixels);
        }
        protected override void Dispose(bool disposing) { if (disposing) data?.Dispose(); base.Dispose(disposing); }
    }
}
