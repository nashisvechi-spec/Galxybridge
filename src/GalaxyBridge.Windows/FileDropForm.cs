using GalaxyBridge.Core;
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
    private readonly CheckBox receive = new() { Text = "Принимать файлы с сопряжённого телефона", AutoSize = true };
    private readonly TextBox folder = new() { ReadOnly = true, Dock = DockStyle.Top };
    private readonly Label incoming = new() { AutoSize = true, MaximumSize = new Size(440, 0), Dock = DockStyle.Top };
    private readonly Button chooseFolder = new() { Text = "Изменить папку…", AutoSize = true };
    private readonly Button cancelIncoming = new() { Text = "Отменить приём", AutoSize = true, Enabled = false };
    private bool updating;
    private bool canSend;

    public FileDropForm(Func<string[], Task> send, Action cancelTransfer, Action<bool> allowReceive,
        Action selectFolder, Action openFolder, Action cancelReceive)
    {
        Icon = AppIcon.Value;
        this.send = send;
        Text = "Galaxy Bridge • Файлы"; Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent; Size = new Size(540, 540);
        MinimumSize = new Size(470, 460); TopMost = true;
        TableLayoutPanel page = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(connection, 0, 0); page.Controls.Add(drop, 0, 1); page.Controls.Add(progress, 0, 2);
        FlowLayoutPanel actions = new() { AutoSize = true, Dock = DockStyle.Fill };
        CheckBox onTop = new() { Text = "Поверх окон", AutoSize = true, Checked = true };
        onTop.CheckedChanged += (_, _) => TopMost = onTop.Checked;
        actions.Controls.Add(choose); actions.Controls.Add(cancel); actions.Controls.Add(onTop); page.Controls.Add(actions, 0, 3);
        TableLayoutPanel received = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        received.Controls.Add(receive, 0, 0); received.Controls.Add(folder, 0, 1); received.Controls.Add(incoming, 0, 2);
        FlowLayoutPanel receiveActions = new() { AutoSize = true, Dock = DockStyle.Fill };
        Button open = new() { Text = "Открыть папку", AutoSize = true };
        receiveActions.Controls.Add(chooseFolder); receiveActions.Controls.Add(open); receiveActions.Controls.Add(cancelIncoming);
        received.Controls.Add(receiveActions, 0, 3); page.Controls.Add(received, 0, 4);
        receive.CheckedChanged += (_, _) => { if (!updating) allowReceive(receive.Checked); };
        chooseFolder.Click += (_, _) => selectFolder(); open.Click += (_, _) => openFolder(); cancelIncoming.Click += (_, _) => cancelReceive();
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
    public void UpdateReceive(bool enabled, string destination, LanReceiveState state)
    {
        if (IsDisposed) return;
        updating = true; receive.Checked = enabled; updating = false;
        if (folder.Text != destination) folder.Text = destination;
        string progress = state.Busy ? "\n" + (state.Size > 0 ? $"{state.Bytes * 100 / state.Size}% · " : "") +
            $"{state.Bytes / 1048576d:N1} МБ" : "";
        string text = state.Message + progress;
        if (incoming.Text != text) incoming.Text = text;
        chooseFolder.Enabled = !state.Busy; cancelIncoming.Enabled = state.Busy;
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
