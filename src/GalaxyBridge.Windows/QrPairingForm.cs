using System.Security.Cryptography;
using GalaxyBridge.Core;
using QRCoder;

namespace GalaxyBridge.Windows;

internal sealed class QrPairingForm : Form
{
    private readonly AdbClient adb;
    private readonly CancellationTokenSource lifetime;
    private readonly Label state = new() { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 12, 0, 12) };
    private readonly QrView qr;
    private readonly CancellationTokenRegistration externalCancel;
    private AdbQrPairing? credentials;
    private bool cancelledByUser;
    public AdbEndpoint? PairedEndpoint { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;

    public QrPairingForm(AdbClient adb, CancellationToken ct)
    {
        this.adb = adb;
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
            "Подключите телефон и ноутбук к одной Wi-Fi-сети. На S25: Параметры разработчика → Беспроводная отладка → Сопряжение по QR-коду. Сканируйте этот код. Он действует 2 минуты." });
        panel.Controls.Add(qr); state.Text = "Ожидаем сканирования на телефоне…"; panel.Controls.Add(state);
        Button cancel = new() { Text = "Закрыть", AutoSize = true, MinimumSize = new Size(120, 36) };
        cancel.Click += (_, _) => { cancelledByUser = true; Close(); }; panel.Controls.Add(cancel); CancelButton = cancel;
        Controls.Add(panel);
        Shown += (_, _) => { if (ct.IsCancellationRequested) Close(); else Completion = PairAsync(); };
        FormClosing += (_, _) => { lifetime.Cancel(); credentials = null; qr.Clear(); };
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
            while (true)
            {
                IReadOnlyList<AdbMdnsService> services = await adb.MdnsAsync(lifetime.Token);
                AdbMdnsService? service = services.FirstOrDefault(active.Matches);
                if (service is not null)
                {
                    state.Text = "Телефон найден. Выполняем сопряжение…";
                    await adb.PairQrAsync(service.Endpoint, active, lifetime.Token);
                    lifetime.Token.ThrowIfCancellationRequested();
                    PairedEndpoint = service.Endpoint; DialogResult = DialogResult.OK; Close(); return;
                }
                await Task.Delay(1000, lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !Disposing && !cancelledByUser)
            { qr.Clear(); state.Text = "Ожидание завершено. Закройте окно и откройте новый QR-код. Если телефон не обнаружен, используйте сопряжение кодом."; }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            if (!IsDisposed && !Disposing)
            { qr.Clear(); state.Text = "Сопряжение не выполнено. Проверьте беспроводную отладку, общую сеть и разрешение ADB в брандмауэре Windows. Можно использовать сопряжение кодом."; }
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
