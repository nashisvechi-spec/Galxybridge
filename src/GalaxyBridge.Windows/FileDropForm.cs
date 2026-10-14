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
        BackColor = BridgeTheme.Background, ForeColor = BridgeTheme.Text,
        Text = "Перетащите файлы сюда\n\nНа телефон → Download/GalaxyBridge\nМожно выбрать несколько файлов. Папки не поддерживаются."
    };
    private readonly Button choose = new BridgeButton { Text = "Выбрать файлы…", AutoSize = true };
    private readonly Button cancel = new BridgeButton { Text = "Отменить отправку", AutoSize = true, Enabled = false };
    private readonly CheckBox receive = new() { Text = "Принимать файлы с сопряжённого телефона", AutoSize = true };
    private readonly TextBox folder = new() { ReadOnly = true, Dock = DockStyle.Top };
    private readonly Label incoming = new() { AutoSize = true, MaximumSize = new Size(440, 0), Dock = DockStyle.Top };
    private readonly Button chooseFolder = new BridgeButton { Text = "Изменить папку…", AutoSize = true };
    private readonly Button cancelIncoming = new BridgeButton { Text = "Отменить приём", AutoSize = true, Enabled = false };
    private bool updating;
    private bool canSend;

    public FileDropForm(Func<string[], Task> send, Action cancelTransfer, Action<bool> allowReceive,
        Action selectFolder, Action openFolder, Action cancelReceive)
    {
        Icon = AppIcon.Value;
        this.send = send;
        Text = "Galaxy Bridge • Файлы"; Font = new Font("Segoe UI", 10);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        StartPosition = FormStartPosition.CenterParent; Size = new Size(640, 650);
        MinimumSize = new Size(470, 460); TopMost = true;
        Shown+=(_,_)=>BridgeTheme.FitWindow(this);
        TableLayoutPanel page=BridgeTheme.Page(this,"Файлы","Отправка и приём через Galaxy Bridge.");
        BridgeCard sending=BridgeTheme.Card(page,"ПК → телефон");
        connection.Dock=DockStyle.None;progress.Dock=DockStyle.None;drop.Dock=DockStyle.None;drop.Size=new Size(450,140);
        sending.Controls.Add(connection);sending.Controls.Add(drop);sending.Controls.Add(progress);
        CheckBox onTop=new() { Text="Поверх окон",AutoSize=true,Checked=true };onTop.CheckedChanged+=(_,_)=>TopMost=onTop.Checked;
        sending.Controls.Add(BridgeTheme.Row(choose,cancel,onTop));
        BridgeCard received=BridgeTheme.Card(page,"Телефон → ПК");
        folder.Dock=DockStyle.None;incoming.Dock=DockStyle.None;received.Controls.Add(receive);received.Controls.Add(folder);received.Controls.Add(incoming);
        BridgeButton open=BridgeTheme.Button("Открыть папку");received.Controls.Add(BridgeTheme.Row(chooseFolder,open,cancelIncoming));
        receive.CheckedChanged+=(_,_)=> { if(!updating) allowReceive(receive.Checked); };
        chooseFolder.Click+=(_,_)=>selectFolder();open.Click+=(_,_)=>openFolder();cancelIncoming.Click+=(_,_)=>cancelReceive();
        choose.Click += async (_, _) =>
        {
            if (!canSend) return;
            using OpenFileDialog picker = new() { Multiselect = true, Title = "Отправить на телефон" };
            if (picker.ShowDialog(this) == DialogResult.OK) await send(picker.FileNames);
        };
        cancel.Click += (_, _) => cancelTransfer();
        BridgeTheme.Apply(this);RegisterDrop(this);
    }
    public void UpdateReceive(bool enabled, string destination, LanReceiveState state)
    {
        if (IsDisposed) return;
        updating = true; receive.Checked = enabled;receive.Enabled=!state.Busy; updating = false;
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
        drop.BackColor = available ? BridgeTheme.Background : BridgeTheme.Surface;
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
