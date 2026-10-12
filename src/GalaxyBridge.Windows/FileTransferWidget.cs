using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GalaxyBridge.Windows;

/** Independent of the main window's minimized/tray state; uses the same transfer. */
internal sealed class FileTransferWidget : Form
{
    private readonly Func<string[], Task> send;
    private readonly Action cancelTransfer;
    private readonly Bitmap artwork;
    private readonly ToolTip tip = new();
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 80 };
    private bool available, busy, choosing, dropping, submitting, disposed;
    private int? percent;
    private int animationStep;
    private string phone = "Ожидаем подключение S25…", message = "";
    private FileWidgetAction pressed, hover, keyboardAction = FileWidgetAction.Send;

    protected override CreateParams CreateParams
    {
        get { CreateParams value = base.CreateParams;value.ExStyle |= 0x00080000;return value; } // WS_EX_LAYERED
    }
    public FileTransferWidget(Func<string[], Task> send, Action cancelTransfer)
    {
        this.send = send;this.cancelTransfer = cancelTransfer;
        using Stream stream = typeof(FileTransferWidget).Assembly.GetManifestResourceStream("GalaxyBridge.FileWidget.png")
            ?? throw new InvalidOperationException("Missing file widget artwork.");
        using Image image = Image.FromStream(stream);artwork = new Bitmap(image);
        Text = "Передача файлов · Galaxy Bridge";Icon = AppIcon.Value;Font = new Font("Segoe UI", 9);
        AccessibleName = "Виджет передачи файлов на телефон";
        AutoScaleMode = AutoScaleMode.Dpi;AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.None;ShowInTaskbar = false;
        MaximizeBox = false;MinimizeBox = false;TopMost = true;StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(420, 247);AllowDrop = true;KeyPreview = true;
        animation.Tick += (_, _) => { animationStep = (animationStep + 1) % 24;Render(); };
        Shown += (_, _) => { BridgeTheme.FitWindow(this);Render(); };
        DragEnter += (_, e) => UpdateDrop(e);
        DragOver += (_, e) => UpdateDrop(e);
        DragLeave += (_, _) => { dropping = false;Render(); };
        DragDrop += async (_, e) => {
            string[]? paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
            bool allowed = FileWidgetLayout.Hit(PointToClient(new Point(e.X, e.Y)), ClientSize, busy) == FileWidgetAction.Send &&
                FileWidgetLayout.CanDrop(available && !choosing && !submitting, busy, paths);
            dropping = false;Render();
            if (allowed) await SendFilesAsync(paths!);
        };
    }

    private void UpdateDrop(DragEventArgs e)
    {
        bool allowed = FileWidgetLayout.Hit(PointToClient(new Point(e.X, e.Y)), ClientSize, busy) == FileWidgetAction.Send &&
            FileWidgetLayout.CanDrop(available && !choosing && !submitting, busy, e.Data?.GetData(DataFormats.FileDrop) as string[]);
        e.Effect = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        if (dropping != allowed) { dropping = allowed;Render(); }
    }
    private async Task ChooseAsync()
    {
        if (!available || busy || choosing || submitting) return;
        choosing = true;
        try
        {
            using OpenFileDialog picker = new() { Multiselect = true, Title = "Отправить на S25" };
            if (picker.ShowDialog(this) == DialogResult.OK && FileWidgetLayout.CanDrop(available, busy, picker.FileNames))
                await SendFilesAsync(picker.FileNames);
        }
        finally { choosing = false;if (!IsDisposed) Render(); }
    }
    private async Task SendFilesAsync(string[] paths)
    {
        // Gate before awaiting: a second drop/click cannot start another transfer.
        if (submitting || !FileWidgetLayout.CanDrop(available, busy, paths)) return;
        submitting = true;busy = true;Render();
        try { await send(paths); }
        finally { submitting = false;if (!IsDisposed) { busy = false;animation.Stop();Render(); } }
    }
    private async Task ActivateAsync(FileWidgetAction action)
    {
        if (IsDisposed) return;
        if (action == FileWidgetAction.Close) Close();
        else if (action == FileWidgetAction.Cancel && busy) cancelTransfer();
        else if (action == FileWidgetAction.Send) await ChooseAsync();
    }

    public void UpdateTransfer(bool canSend, bool busy, string phone, string message, int? percent)
    {
        if (IsDisposed) return;
        bool changed = available != canSend || this.busy != busy || this.phone != phone || this.message != message || this.percent != percent;
        available = canSend;this.busy = busy;this.phone = phone;this.message = message;
        this.percent = percent.HasValue ? Math.Clamp(percent.Value, 0, 100) : null;
        if (busy && !percent.HasValue) animation.Start();else animation.Stop();
        if (changed) { UpdateTip(hover);Render(); }
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e);Render(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e);Render(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e);Render(); }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e);Render(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e);Render(); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        FileWidgetAction action = FileWidgetLayout.Hit(e.Location, ClientSize, busy);
        Cursor = action == FileWidgetAction.Move ? Cursors.SizeAll :
            action == FileWidgetAction.Send && (!available || busy) ? Cursors.No : Cursors.Hand;
        if (hover != action) { hover = action;UpdateTip(action);Render(); }
    }
    private void UpdateTip(FileWidgetAction action)
    {
        string text = action switch {
            FileWidgetAction.Close => "Закрыть виджет",
            FileWidgetAction.Cancel => "Отменить передачу",
            FileWidgetAction.Move => "Переместить виджет",
            _ => busy ? message : available ? "Перетащите файлы или нажмите для выбора\n" + phone +
                (string.IsNullOrEmpty(message) ? "" : "\n" + message) : phone
        };
        tip.SetToolTip(this, text);
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e);hover = FileWidgetAction.None;Render(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        pressed = FileWidgetLayout.Hit(e.Location, ClientSize, busy);
        if (pressed == FileWidgetAction.Move) { pressed = FileWidgetAction.None;AlphaWidgetWindow.Move(Handle); }
        else { Capture = true;keyboardAction = pressed; }
    }
    protected override async void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        FileWidgetAction action = pressed;pressed = FileWidgetAction.None;Capture = false;
        if (action != FileWidgetAction.None && action == FileWidgetLayout.Hit(e.Location, ClientSize, busy)) await ActivateAsync(action);
    }
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape) { Close();return true; }
        if ((keyData & Keys.KeyCode) == Keys.Tab) {
            FileWidgetAction[] actions = busy ? [FileWidgetAction.Cancel, FileWidgetAction.Close] : [FileWidgetAction.Send, FileWidgetAction.Close];
            int index = Array.IndexOf(actions, keyboardAction), direction = (keyData & Keys.Shift) != 0 ? -1 : 1;
            keyboardAction = actions[(index + direction + actions.Length) % actions.Length];Render();return true;
        }
        if ((keyData & Keys.KeyCode) is Keys.Enter or Keys.Space) { _ = ActivateAsync(keyboardAction);return true; }
        return base.ProcessDialogKey(keyData);
    }
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) { e.Handled = true;e.SuppressKeyPress = true;await ActivateAsync(keyboardAction); }
    }

    private void Render()
    {
        // Size/handle callbacks can arrive before the constructor loads the image.
        if (disposed || artwork is null || !IsHandleCreated || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using Bitmap frame = new(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(frame))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(artwork, new Rectangle(Point.Empty, ClientSize));
            g.ScaleTransform(Width / (float)FileWidgetLayout.ArtWidth, Height / (float)FileWidgetLayout.ArtHeight);
            if (dropping || hover == FileWidgetAction.Send && available && !busy) {
                using GraphicsPath outline = BridgeTheme.Round(FileWidgetLayout.SendBounds, 65);
                using Pen glow = new(Color.FromArgb(170, 112, 255, 255), 9);g.DrawPath(glow, outline);
            }
            if (Focused && ShowFocusCues) {
                RectangleF focus = keyboardAction switch { FileWidgetAction.Close => FileWidgetLayout.CloseBounds,
                    FileWidgetAction.Cancel => FileWidgetLayout.CancelBounds, _ => FileWidgetLayout.SendBounds };
                using Pen cue = new(Color.White, 5) { DashStyle = DashStyle.Dot };
                using GraphicsPath outline = BridgeTheme.Round(focus, 35);g.DrawPath(cue, outline);
            }
            if (!available || busy) {
                RectangleF badge = new(180, 635, 1290, 110);
                using GraphicsPath path = BridgeTheme.Round(badge, 30);
                using SolidBrush shade = new(Color.FromArgb(235, 5, 24, 43));g.FillPath(shade, path);
                using Font textFont = new("Segoe UI", 35, FontStyle.Regular, GraphicsUnit.Pixel);
                using SolidBrush ink = new(Color.FromArgb(230, 247, 255));
                using StringFormat format = new() { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
                string text = busy ? (string.IsNullOrEmpty(message) ? "Передача файлов…" : message) : phone;
                g.DrawString(text, textFont, ink, new RectangleF(205, 644, busy ? 1020 : 1240, 65), format);
                if (busy) {
                    using SolidBrush cyan = new(Color.FromArgb(80, 243, 250));
                    float width = percent.HasValue ? 1015 * percent.Value / 100f : 180;
                    float left = percent.HasValue ? 205 : 205 + animationStep * (835f / 23);
                    g.FillRectangle(cyan, left, 718, width, 8);
                    using GraphicsPath cancel = BridgeTheme.Round(FileWidgetLayout.CancelBounds, 24);
                    using Pen edge = new(Color.FromArgb(90, 231, 242), 4);g.DrawPath(edge, cancel);
                    format.Alignment = StringAlignment.Center;g.DrawString("Отмена", textFont, ink, FileWidgetLayout.CancelBounds, format);
                }
            }
        }
        AlphaWidgetWindow.Present(Handle, frame, Location);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new WidgetAccessibleObject(this);
    private sealed class WidgetAccessibleObject(FileTransferWidget owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Pane;
        public override int GetChildCount() => owner.busy ? 3 : 2;
        public override AccessibleObject? GetChild(int index) => index switch {
            0 => new ActionAccessibleObject(owner, FileWidgetAction.Send),
            1 => new ActionAccessibleObject(owner, FileWidgetAction.Close),
            2 when owner.busy => new ActionAccessibleObject(owner, FileWidgetAction.Cancel), _ => null
        };
    }
    private sealed class ActionAccessibleObject(FileTransferWidget owner, FileWidgetAction action) : AccessibleObject
    {
        public override string? Name { get => action switch { FileWidgetAction.Close => "Закрыть виджет",
            FileWidgetAction.Cancel => "Отменить передачу", _ => "Выбрать файлы для отправки на телефон" };set { } }
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override Rectangle Bounds => owner.RectangleToScreen(FileWidgetLayout.Bounds(action, owner.ClientSize));
        public override AccessibleObject? Parent => owner.AccessibilityObject;
        public override string? DefaultAction => "Нажать";
        public override AccessibleStates State => AccessibleStates.Focusable |
            (action == FileWidgetAction.Send && (!owner.available || owner.busy) ? AccessibleStates.Unavailable : AccessibleStates.None);
        public override void DoDefaultAction() {
            if (!owner.IsDisposed && owner.IsHandleCreated) owner.BeginInvoke(new Action(async () => await owner.ActivateAsync(action)));
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true;animation.Stop();animation.Dispose();tip.Dispose();artwork.Dispose(); }
        base.Dispose(disposing);
    }
}
