namespace GalaxyBridge.Windows;

// A tray balloon can be silently suppressed. This native card also works when
// the main window is hidden, without activating it or taking keyboard focus.
internal sealed class ReceiptNotice : Form
{
    private readonly Label title = new() { AutoSize=true, Font=new Font("Segoe UI",12,FontStyle.Bold) };
    private readonly Label detail = new() { AutoSize=true, UseMnemonic=false };
    private readonly System.Windows.Forms.Timer dismiss = new() { Interval=10000 };
    private Action? openFolder;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { CreateParams value=base.CreateParams;value.ExStyle|=0x08000000;return value; } // WS_EX_NOACTIVATE
    }
    public ReceiptNotice()
    {
        Text="Galaxy Bridge";Icon=AppIcon.Value;Font=new Font("Segoe UI",10);
        AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        FormBorderStyle=FormBorderStyle.FixedToolWindow;ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;TopMost=true;ClientSize=new Size(360,170);
        FlowLayoutPanel content=new() { Dock=DockStyle.Fill,Padding=new Padding(16),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true };
        title.MaximumSize=new Size(320,0);detail.MaximumSize=new Size(320,0);
        content.Controls.Add(title);content.Controls.Add(detail);
        BridgeButton open=BridgeTheme.Button("Открыть папку");open.Click+=(_,_)=> { Action? action=openFolder;Close();action?.Invoke(); };
        BridgeButton close=BridgeTheme.Button("Закрыть");close.Click+=(_,_)=>Close();
        content.Controls.Add(BridgeTheme.Row(open,close));Controls.Add(content);
        dismiss.Tick+=(_,_)=> { if(!Bounds.Contains(Cursor.Position)) Close(); };
    }
    public void Present(string heading,string message,Rectangle work,Action open)
    {
        title.Text=heading;detail.Text=message;openFolder=open;BridgeTheme.Apply(this);
        if(!Visible) Show();
        Size=new Size(Math.Min(Width,work.Width),Math.Min(Height,work.Height));
        Location=new Point(Math.Max(work.Left,work.Right-Width-16),Math.Max(work.Top,work.Bottom-Height-16));
        dismiss.Stop();dismiss.Start();
    }
    protected override void Dispose(bool disposing)
    { if(disposing) { dismiss.Dispose();openFolder=null; }base.Dispose(disposing); }
}
