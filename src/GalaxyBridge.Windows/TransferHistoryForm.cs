using System.Diagnostics;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed class TransferHistoryForm : Form
{
    private sealed record Row(TransferEntry Entry) {
        public override string ToString() => $"{Entry.Time.ToLocalTime():dd.MM HH:mm} · {Entry.Direction} · {Entry.Status} · {Entry.Name}";
    }
    private readonly TransferJournal journal;
    private readonly ListBox list = new() { Dock=DockStyle.Fill,HorizontalScrollbar=true,IntegralHeight=false };
    private readonly TextBox details = new() { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical };
    private readonly BridgeButton retry = BridgeTheme.Button("Повторить отправку");
    private readonly System.Windows.Forms.Timer refresh = new() { Interval=500 };
    public TransferHistoryForm(TransferJournal journal,Func<string[],Task> send,Func<bool> canSend)
    {
        this.journal=journal;Text="История передач · Galaxy Bridge";Icon=AppIcon.Value;Font=new Font("Segoe UI",10);
        AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);Size=new Size(900,620);MinimumSize=new Size(500,400);StartPosition=FormStartPosition.CenterParent;
        TableLayoutPanel shell=new() { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(16) };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,110));shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(BridgeTheme.Label("Последние 100 передач. Повтор — на текущий подключённый телефон. Очистка истории не удаляет файлы."),0,0);shell.Controls.Add(list,0,1);shell.Controls.Add(details,0,2);
        BridgeButton open=BridgeTheme.Button("Открыть файл"),folder=BridgeTheme.Button("Открыть папку"),clear=BridgeTheme.Button("Очистить историю");
        FlowLayoutPanel actions=BridgeTheme.Row(open,folder,retry,clear);actions.Dock=DockStyle.Fill;shell.Controls.Add(actions,0,3);Controls.Add(shell);
        list.SelectedIndexChanged+=(_,_)=> { TransferEntry? entry=(list.SelectedItem as Row)?.Entry;details.Text=entry is null?"":$"{entry.Name}\r\n{entry.Status}\r\n{entry.Error}\r\n{entry.LocalPath ?? entry.Source}"; };
        string? SelectedPath() { var entry=(list.SelectedItem as Row)?.Entry;return entry?.LocalPath??entry?.Source; }
        void Open(bool directory) {
            string? path=SelectedPath();if(path is null) return;if(directory) path=Path.GetDirectoryName(path);
            try { if(path is not null && (File.Exists(path)||Directory.Exists(path))) Process.Start(new ProcessStartInfo(path) { UseShellExecute=true });else details.Text="Файл перемещён или удалён. Выберите его заново."; }
            catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { details.Text="Не удалось открыть файл или папку."; }
        }
        open.Click+=(_,_)=>Open(false);folder.Click+=(_,_)=>Open(true);
        retry.Click+=async(_,_)=> { if((list.SelectedItem as Row)?.Entry.Source is string source && File.Exists(source) && canSend()) { await send([source]);RefreshEntries(); } };
        clear.Click+=(_,_)=> {
            if(MessageBox.Show(this,"Очистить только историю передач? Файлы сохранятся.","История",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.OK) return;
            try { journal.Clear();RefreshEntries(); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { details.Text="Историю не удалось сохранить."; }
        };
        refresh.Tick+=(_,_)=> { var entry=(list.SelectedItem as Row)?.Entry;retry.Enabled=canSend() && entry?.Source is string source && File.Exists(source); };
        Shown+=(_,_)=> { BridgeTheme.FitWindow(this);refresh.Start(); };BridgeTheme.Apply(this);RefreshEntries();
    }
    public void RefreshEntries()
    {
        string? selected=(list.SelectedItem as Row)?.Entry.Id;list.BeginUpdate();list.Items.Clear();
        foreach(var entry in journal.Entries) list.Items.Add(new Row(entry));
        for(int i=0;i<list.Items.Count;i++) if(((Row)list.Items[i]).Entry.Id==selected) list.SelectedIndex=i;
        if(list.SelectedIndex<0 && list.Items.Count>0) list.SelectedIndex=0;list.EndUpdate();
    }
    protected override void Dispose(bool disposing) { if(disposing) refresh.Dispose();base.Dispose(disposing); }
}
