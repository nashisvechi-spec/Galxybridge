namespace GalaxyBridge.Windows;

internal sealed class FileDropForm : Form
{
    private readonly Func<string[], Task> send;
    private readonly Label connection = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly Label progress = new() { AutoSize = true, MaximumSize = new Size(440, 0), Dock = DockStyle.Top };
    private readonly Label drop = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.FromArgb(235, 243, 255), ForeColor = Color.FromArgb(25, 65, 120),
        Text = "Перетащите файлы сюда\n\nНа телефон → Download/GalaxyBridge\nМожно выбрать несколько файлов. Папки не поддерживаются."
    };
    private readonly Button choose = new() { Text = "Выбрать файлы…", AutoSize = true };
    private readonly Button cancel = new() { Text = "Отменить отправку", AutoSize = true, Enabled = false };
    private bool canSend;

    public FileDropForm(Func<string[], Task> send, Action cancelTransfer)
    {
        this.send = send;
        Text = "Galaxy Bridge • Отправка файлов"; Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent; Size = new Size(500, 360);
        MinimumSize = new Size(430, 320); TopMost = true;
        TableLayoutPanel page = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(connection, 0, 0); page.Controls.Add(drop, 0, 1); page.Controls.Add(progress, 0, 2);
        FlowLayoutPanel actions = new() { AutoSize = true, Dock = DockStyle.Fill };
        CheckBox onTop = new() { Text = "Поверх окон", AutoSize = true, Checked = true };
        onTop.CheckedChanged += (_, _) => TopMost = onTop.Checked;
        actions.Controls.Add(choose); actions.Controls.Add(cancel); actions.Controls.Add(onTop); page.Controls.Add(actions, 0, 3);
        Controls.Add(page);
        choose.Click += async (_, _) =>
        {
            if (!canSend) return;
            using OpenFileDialog picker = new() { Multiselect = true, Title = "Отправить на телефон" };
            if (picker.ShowDialog(this) == DialogResult.OK) await send(picker.FileNames);
        };
        cancel.Click += (_, _) => cancelTransfer();
        RegisterDrop(this);
    }
    public void UpdateTransfer(bool available, bool busy, string phone, string message)
    {
        if (IsDisposed) return;
        canSend = available; choose.Enabled = available; cancel.Enabled = busy;
        connection.Text = phone;
        if (progress.Text != message) progress.Text = message;
        drop.BackColor = available ? Color.FromArgb(235, 243, 255) : SystemColors.Control;
    }
    private void RegisterDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            e.Effect = canSend && e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && paths.All(File.Exists)
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        control.DragDrop += async (_, e) =>
        { if (canSend && e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await send(paths); };
        foreach (Control child in control.Controls) RegisterDrop(child);
    }
}
