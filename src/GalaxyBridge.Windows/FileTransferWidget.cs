namespace GalaxyBridge.Windows;

/** Independent of the main window's minimized/tray state; uses the same transfer. */
internal sealed class FileTransferWidget : Form
{
    private bool available;
    private readonly Label connection = new() { AutoSize = true, MaximumSize = new Size(310, 0) };
    private readonly Label status = new() { AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(310, 48), Text = "На телефон → Download/GalaxyBridge" };
    private readonly Label target = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Text = "Перетащите файлы сюда\nдля отправки на S25", BackColor = BridgeTheme.Background, ForeColor = BridgeTheme.Text };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 10, Visible = false, MarqueeAnimationSpeed = 30 };
    private readonly Button choose = new BridgeButton { Text = "Выбрать…", AutoSize = true };
    private readonly Button cancel = new BridgeButton { Text = "Отмена", AutoSize = true, Enabled = false };
    public FileTransferWidget(Func<string[], Task> send, Action cancelTransfer)
    {
        Text = "На S25 · Galaxy Bridge"; Icon = AppIcon.Value; Font = new Font("Segoe UI", 10);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        FormBorderStyle = FormBorderStyle.SizableToolWindow; ShowInTaskbar = false;
        TopMost = true; StartPosition = FormStartPosition.Manual;
        Size = new Size(350, 290); MinimumSize = new Size(320, 260);
        Shown+=(_,_)=>BridgeTheme.FitWindow(this);
        TableLayoutPanel page = new() { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 5 };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(connection, 0, 0);page.Controls.Add(target, 0, 1);page.Controls.Add(progress, 0, 2);page.Controls.Add(status, 0, 3);
        FlowLayoutPanel actions = new() { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.Add(choose);actions.Controls.Add(cancel);page.Controls.Add(actions, 0, 4);Controls.Add(page);
        choose.Click += async (_, _) => {
            if (!available) return;
            using OpenFileDialog picker = new() { Multiselect = true, Title = "Отправить на S25" };
            if (picker.ShowDialog(this) == DialogResult.OK) await send(picker.FileNames);
        };
        cancel.Click += (_, _) => cancelTransfer();
        void Register(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += (_, e) => e.Effect = available && e.Data?.GetData(DataFormats.FileDrop) is string[] paths &&
                paths.Length is > 0 and <= 100 && paths.All(File.Exists) ? DragDropEffects.Copy : DragDropEffects.None;
            control.DragDrop += async (_, e) => {
                if (available && e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await send(paths);
            };
            foreach (Control child in control.Controls) Register(child);
        }
        BridgeTheme.Apply(this);Register(this);
    }
    public void UpdateTransfer(bool canSend, bool busy, string phone, string message, int? percent)
    {
        if (IsDisposed) return;
        available = canSend;choose.Enabled = canSend;cancel.Enabled = busy;
        if (connection.Text != phone) connection.Text = phone;
        string text = string.IsNullOrEmpty(message) ? "На телефон → Download/GalaxyBridge" : message;
        if (status.Text != text) status.Text = text;
        target.BackColor = canSend ? BridgeTheme.Background : BridgeTheme.Surface;
        progress.Visible = busy;
        progress.Style = percent.HasValue ? ProgressBarStyle.Blocks : ProgressBarStyle.Marquee;
        if (percent.HasValue) progress.Value = Math.Clamp(percent.Value, 0, 100);
    }
}
