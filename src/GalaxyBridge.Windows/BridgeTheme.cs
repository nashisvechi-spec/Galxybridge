using System.Drawing.Drawing2D;
using Microsoft.Win32;
namespace GalaxyBridge.Windows;

internal static class BridgeTheme
{
    public static bool Dark { get; private set; }
    public static Color Background => Dark ? Color.FromArgb(13,20,34) : Color.FromArgb(244,247,252);
    public static Color Surface => Dark ? Color.FromArgb(19,30,48) : Color.White;
    public static Color Text => Dark ? Color.FromArgb(235,242,255) : Color.FromArgb(25,38,62);
    public static Color Muted => Dark ? Color.FromArgb(166,184,210) : Color.FromArgb(83,105,138);
    public static Color Border => Dark ? Color.FromArgb(43,61,86) : Color.FromArgb(216,227,243);
    public static Color Blue => Color.FromArgb(9,99,211);
    public static Color Link => Dark ? Color.FromArgb(73,219,223) : Color.FromArgb(0,112,124);
    public static Color Success => Dark ? Color.FromArgb(117,213,173) : Color.FromArgb(0,115,78);
    public static void Set(string mode)
    {
        bool systemDark=false;
        try { systemDark=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int value && value==0; }
        catch(Exception ex) when(ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        Dark=mode=="dark" || mode=="system" && systemDark;
    }
    public static GraphicsPath Round(RectangleF b,float radius)
    {
        GraphicsPath path=new();float d=Math.Min(radius*2,Math.Min(b.Width,b.Height));
        if(d<=0) { path.AddRectangle(b);return path; }
        path.AddArc(b.Left,b.Top,d,d,180,90);path.AddArc(b.Right-d,b.Top,d,d,270,90);
        path.AddArc(b.Right-d,b.Bottom-d,d,d,0,90);path.AddArc(b.Left,b.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    public static void FitWindow(Form form)
    {
        Rectangle work=Screen.FromControl(form).WorkingArea;
        int width=Math.Max(320,work.Width-24),height=Math.Max(240,work.Height-24);
        form.MinimumSize=new Size(Math.Min(form.MinimumSize.Width,width),Math.Min(form.MinimumSize.Height,height));
        form.Size=new Size(Math.Min(form.Width,width),Math.Min(form.Height,height));
        form.Location=new Point(Math.Clamp(form.Left,work.Left,Math.Max(work.Left,work.Right-form.Width)),
            Math.Clamp(form.Top,work.Top,Math.Max(work.Top,work.Bottom-form.Height)));
    }
    public static Label Label(string text,bool heading=false) => new() {
        Text=text,AutoSize=true,MaximumSize=new Size(650,0),Margin=new Padding(0,0,0,heading?14:10),
        Font=new Font("Segoe UI",heading?18:10,heading?FontStyle.Bold:FontStyle.Regular),Tag=heading?"heading":"muted"
    };
    public static BridgeButton Button(string text,bool primary=false) => new() { Text=text,Primary=primary };
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        FlowLayoutPanel row=new() { AutoSize=true,WrapContents=true,Margin=new Padding(0,4,0,8),MaximumSize=new Size(650,0) };
        row.Controls.AddRange(controls);return row;
    }
    public static TableLayoutPanel Page(Control parent,string title,string subtitle)
    {
        Panel scroll=new() { Dock=DockStyle.Fill,AutoScroll=true };
        TableLayoutPanel content=new() { Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=0,Padding=new Padding(24) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(content);parent.Controls.Add(scroll);
        Add(content,Label(title,true));Add(content,Label(subtitle));return content;
    }
    public static void Add(TableLayoutPanel parent,Control child)
    {
        int row=parent.RowCount++;parent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        child.Anchor=AnchorStyles.Left|AnchorStyles.Right;parent.Controls.Add(child,0,row);
    }
    public static BridgeCard Card(TableLayoutPanel parent,string title)
    {
        BridgeCard card=new();card.Controls.Add(new Label { Text=title,AutoSize=true,Font=new Font("Segoe UI",12,FontStyle.Bold),Margin=new Padding(0,0,0,14) });
        Add(parent,card);return card;
    }
    public static void Apply(Control root)
    {
        root.ForeColor=root.Tag as string=="muted" ? Muted : Text;
        root.BackColor=root is Form || root is Panel && root is not BridgeCard && root.Parent is not BridgeCard ? Background : Surface;
        if(root is BridgeCard || root.Tag as string=="surface") root.BackColor=Surface;
        if(root is Button button) {
            button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.UseVisualStyleBackColor=false;
        }
        if(root is ComboBox combo) combo.FlatStyle=FlatStyle.Flat;
        if(root is TextBox box) box.BorderStyle=BorderStyle.FixedSingle;
        if(root is PictureBox) root.BackColor=root.Tag as string=="brand"?Surface:Color.White;
        foreach(Control child in root.Controls) Apply(child);
        root.Invalidate();
    }
}

internal sealed class BridgeButton : Button
{
    public bool Primary { get; set; }
    public bool Selected { get; set; }
    public BridgeButton() { AutoSize=true;MinimumSize=new Size(100,42);Margin=new Padding(0,0,10,8);Padding=new Padding(14,8,14,8);FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor??BridgeTheme.Background);
        Color fill=Primary && Enabled?BridgeTheme.Blue:Selected?BridgeTheme.Dark?Color.FromArgb(28,57,92):Color.FromArgb(226,239,255):BridgeTheme.Surface;
        using GraphicsPath path=BridgeTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),10*DeviceDpi/96f);
        using SolidBrush brush=new(fill);e.Graphics.FillPath(brush,path);
        if(!Primary) { using Pen border=new(Selected?BridgeTheme.Blue:BridgeTheme.Border);e.Graphics.DrawPath(border,path); }
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,!Enabled?BridgeTheme.Muted:Primary?Color.White:Selected?BridgeTheme.Dark?Color.FromArgb(133,191,255):BridgeTheme.Blue:BridgeTheme.Text,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),Primary?Color.White:BridgeTheme.Link,fill);
    }
}

internal sealed class BridgeCard : FlowLayoutPanel
{
    public BridgeCard()
    {
        AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FlowDirection=FlowDirection.TopDown;WrapContents=false;
        Padding=new Padding(20);Margin=new Padding(0,6,0,16);Dock=DockStyle.Fill;DoubleBuffered=true;
        SizeChanged+=(_,_)=>Fit();ControlAdded+=(_,_)=>Fit();
    }
    private void Fit()
    {
        int available=Math.Max(150,ClientSize.Width-Padding.Horizontal);
        foreach(Control child in Controls) {
            if(child is Label or FlowLayoutPanel) child.MaximumSize=new Size(available,0);
            if(child is DeviceMap or TextBox || child is Label { AutoSize:false }) child.Width=available;
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent?.BackColor??BridgeTheme.Background);
        using GraphicsPath path=BridgeTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),16*DeviceDpi/96f);
        using SolidBrush fill=new(BridgeTheme.Surface);using Pen border=new(BridgeTheme.Border);
        e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);
    }
}
