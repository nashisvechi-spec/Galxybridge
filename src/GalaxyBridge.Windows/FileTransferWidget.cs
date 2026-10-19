using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GalaxyBridge.Windows;

/** Independent of the main window's minimized/tray state; uses the same transfer. */
internal sealed class FileTransferWidget : Form
{
    private readonly Func<string[], Task> send;
    private readonly Action cancelTransfer;
    private readonly ToolTip tip = new();
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 80 };
    private bool available, busy, choosing, dropping, submitting, disposed, initialized, shown, layoutQueued, sizing;
    private int? percent;
    private int animationStep;
    private byte opacity = 255;
    private string phone = "Ожидаем подключение S25…", message = "";
    private FileWidgetAction pressed, hover, keyboardAction = FileWidgetAction.Send;

    protected override CreateParams CreateParams
    {
        get { CreateParams value = base.CreateParams;value.ExStyle |= 0x00080000;return value; } // WS_EX_LAYERED
    }
    public FileTransferWidget(Func<string[], Task> send, Action cancelTransfer)
    {
        this.send = send;this.cancelTransfer = cancelTransfer;
        Text = "Передача файлов · Galaxy Bridge";Icon = AppIcon.Value;Font = new Font("Segoe UI", 9);
        AccessibleName = "Виджет передачи файлов на телефон";
        // This is one vector canvas, not a control layout. Scale both axes once.
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;ShowInTaskbar = false;
        ControlBox = false;MaximizeBox = false;MinimizeBox = false;TopMost = true;StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(FileWidgetLayout.ArtWidth, FileWidgetLayout.ArtHeight);AllowDrop = true;KeyPreview = true;
        animation.Tick += (_, _) => { animationStep = (animationStep + 1) % 24;Render(); };
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
        initialized = true;
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
        submitting = true;busy = true;keyboardAction = FileWidgetAction.Cancel;Render();
        try { await send(paths); }
        finally { submitting = false;if (!IsDisposed) { busy = false;if (keyboardAction == FileWidgetAction.Cancel) keyboardAction = FileWidgetAction.Send;animation.Stop();Render(); } }
    }
    private async Task ActivateAsync(FileWidgetAction action)
    {
        if (IsDisposed) return;
        if (action == FileWidgetAction.Close) Close();
        else if (action == FileWidgetAction.Cancel && busy) cancelTransfer();
        else if (action == FileWidgetAction.Send) await ChooseAsync();
    }

    public void SetTransparency(int percent)
    {
        byte value = (byte)((255 * (100 - Math.Clamp(percent, 0, 80)) + 50) / 100);
        if (opacity == value) return;
        opacity = value;Render();
    }
    public void UpdateTransfer(bool canSend, bool busy, string phone, string message, int? percent)
    {
        if (IsDisposed) return;
        bool changed = available != canSend || this.busy != busy || this.phone != phone || this.message != message || this.percent != percent;
        available = canSend;this.busy = busy;this.phone = phone;this.message = message;
        if (keyboardAction == FileWidgetAction.Send && busy) keyboardAction = FileWidgetAction.Cancel;
        else if (keyboardAction == FileWidgetAction.Cancel && !busy) keyboardAction = FileWidgetAction.Send;
        this.percent = percent.HasValue ? Math.Clamp(percent.Value, 0, 100) : null;
        if (busy && !percent.HasValue) animation.Start();else animation.Stop();
        if (changed) { UpdateTip(hover);Render(); }
    }
    protected override void OnShown(EventArgs e)
    {
        ApplyWidgetBounds(DeviceDpi);shown = true;base.OnShown(e);Render();
    }
    private void ApplyWidgetBounds(int dpi, Point? location = null)
    {
        if (sizing || IsDisposed) return;
        sizing = true;
        try
        {
            Point wanted = location ?? Location;Rectangle work = Screen.FromPoint(wanted).WorkingArea;
            Size size = FileWidgetLayout.SizeForDpi(dpi, work.Size);
            SetBounds(Math.Clamp(wanted.X, work.Left, Math.Max(work.Left, work.Right - size.Width)),
                Math.Clamp(wanted.Y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)), size.Width, size.Height);
        }
        finally { sizing = false; }
    }
    private void QueueLayout()
    {
        if (!initialized || !shown || sizing || layoutQueued || !IsHandleCreated || IsDisposed) return;
        layoutQueued = true;
        BeginInvoke(new Action(() => {
            layoutQueued = false;
            if (IsDisposed || !IsHandleCreated) return;
            ApplyWidgetBounds(DeviceDpi);Render();
        }));
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e);QueueLayout(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e);QueueLayout(); }
    protected override bool OnGetDpiScaledSize(int oldDpi, int newDpi, ref Size desiredSize)
    {
        desiredSize = FileWidgetLayout.SizeForDpi(newDpi, new Size(int.MaxValue, int.MaxValue));return true;
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true;base.OnDpiChanged(e);ApplyWidgetBounds(e.DeviceDpiNew, e.SuggestedRectangle.Location);QueueLayout();
    }
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
        if (!initialized || !shown || disposed || !IsHandleCreated || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using Bitmap frame = new(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(frame))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF viewport = FileWidgetLayout.Viewport(ClientSize);float scale = viewport.Width / FileWidgetLayout.ArtWidth;
            g.TranslateTransform(viewport.X, viewport.Y);g.ScaleTransform(scale, scale);
            using GraphicsPath body = BridgeTheme.Round(new RectangleF(1, 1, 318, 182), 20);
            using SolidBrush navy = new(Color.FromArgb(24, 36, 56));g.FillPath(navy, body);
            using GraphicsPath target = BridgeTheme.Round(FileWidgetLayout.SendBounds, 12);
            using SolidBrush inset = new(Color.FromArgb(29, 43, 65));g.FillPath(inset, target);
            using SolidBrush dot = new(available ? Color.FromArgb(123, 206, 166) : Color.FromArgb(147, 161, 181));
            g.FillEllipse(dot, 20, 22, 7, 7);
            using Font titleFont = new("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            using SolidBrush ink = new(Color.FromArgb(225, 232, 243));
            using StringFormat format = new() { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
            string title = available ? phone.StartsWith("Телефон: ", StringComparison.Ordinal) ? phone[9..] : phone : "Файлы";
            g.DrawString(title, titleFont, ink, new RectangleF(36, 12, 226, 28), format);
            using SolidBrush closeFill = new(hover == FileWidgetAction.Close ? Color.FromArgb(64, 78, 99) : Color.FromArgb(39, 53, 74));
            g.FillEllipse(closeFill, FileWidgetLayout.CloseBounds);
            using Pen cross = new(Color.FromArgb(195, 206, 223), 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(cross, 288, 22, 296, 30);g.DrawLine(cross, 296, 22, 288, 30);
            using Pen arrow = new(available ? Color.FromArgb(156, 186, 248) : Color.FromArgb(129, 145, 166), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(arrow, [new PointF(124, 89), new PointF(196, 89), new PointF(184, 77)]);
            g.DrawLine(arrow, 196, 89, 184, 101);
            g.DrawLines(arrow, [new PointF(196, 116), new PointF(124, 116), new PointF(136, 104)]);
            g.DrawLine(arrow, 124, 116, 136, 128);
            if (dropping || hover == FileWidgetAction.Send && available && !busy) {
                using Pen edge = new(Color.FromArgb(139, 173, 241), 1.5f);g.DrawPath(edge, target);
            }
            if (Focused && ShowFocusCues) {
                RectangleF focus = keyboardAction switch { FileWidgetAction.Close => FileWidgetLayout.CloseBounds,
                    FileWidgetAction.Cancel => FileWidgetLayout.CancelBounds, _ => FileWidgetLayout.SendBounds };
                using Pen cue = new(Color.White, 1) { DashStyle = DashStyle.Dot };
                using GraphicsPath outline = BridgeTheme.Round(focus, keyboardAction == FileWidgetAction.Close ? 16 : 12);g.DrawPath(cue, outline);
            }
            if (!available || busy) {
                using Font textFont = new("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
                string text = busy ? (string.IsNullOrEmpty(message) ? "Передача файлов…" : message) : "Нет связи";
                format.Alignment = busy ? StringAlignment.Near : StringAlignment.Center;
                g.DrawString(text, textFont, ink, new RectangleF(24, 136, busy ? 208 : 272, 24), format);
                if (busy) {
                    using SolidBrush track = new(Color.FromArgb(56, 72, 96));g.FillRectangle(track, 24, 165, 208, 2);
                    using SolidBrush blue = new(Color.FromArgb(156, 186, 248));
                    float width = percent.HasValue ? 208 * percent.Value / 100f : 32;
                    float left = percent.HasValue ? 24 : 24 + animationStep * (176f / 23);
                    g.FillRectangle(blue, left, 165, width, 2);
                    using GraphicsPath cancel = BridgeTheme.Round(FileWidgetLayout.CancelBounds, 8);
                    using SolidBrush cancelFill = new(hover == FileWidgetAction.Cancel ? Color.FromArgb(64, 78, 99) : Color.FromArgb(39, 53, 74));g.FillPath(cancelFill, cancel);
                    format.Alignment = StringAlignment.Center;g.DrawString("Отмена", textFont, ink, FileWidgetLayout.CancelBounds, format);
                }
            }
        }
        if (!AlphaWidgetWindow.Present(Handle, frame, opacity)) QueueLayout();
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
        if (disposing && !disposed) { disposed = true;animation.Stop();animation.Dispose();tip.Dispose(); }
        base.Dispose(disposing);
    }
}
