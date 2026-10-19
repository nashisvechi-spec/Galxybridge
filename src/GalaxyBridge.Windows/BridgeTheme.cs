using System.Drawing.Drawing2D;
using Microsoft.Win32;
namespace GalaxyBridge.Windows;

internal static class BridgeTheme
{
    public static bool Dark { get; private set; }
    public static Color Background => Dark ? Color.FromArgb(21,24,30) : Color.FromArgb(245,246,248);
    public static Color Surface => Dark ? Color.FromArgb(29,33,41) : Color.White;
    public static Color Text => Dark ? Color.FromArgb(238,240,244) : Color.FromArgb(32,38,48);
    public static Color Muted => Dark ? Color.FromArgb(162,170,184) : Color.FromArgb(99,109,125);
    public static Color Border => Dark ? Color.FromArgb(48,54,65) : Color.FromArgb(228,231,236);
    public static Color Blue => Color.FromArgb(53,107,217);
    public static Color Link => Dark ? Color.FromArgb(143,178,255) : Blue;
    public static Color Success => Dark ? Color.FromArgb(123,206,166) : Color.FromArgb(36,123,82);
    public static Color Selected => Dark ? Color.FromArgb(40,52,75) : Color.FromArgb(235,241,253);
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
        Font=new Font("Segoe UI",heading?20:10,heading?FontStyle.Bold:FontStyle.Regular),Tag=heading?"heading":"muted"
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
        Add(content,Label(title,true));if(!string.IsNullOrEmpty(subtitle)) Add(content,Label(subtitle));return content;
    }
    public static void Add(TableLayoutPanel parent,Control child)
    {
        int row=parent.RowCount++;parent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        child.Anchor=AnchorStyles.Left|AnchorStyles.Right;parent.Controls.Add(child,0,row);
    }
    public static BridgeCard Card(TableLayoutPanel parent,string title)
    {
        BridgeCard card=new();if(!string.IsNullOrEmpty(title)) card.Controls.Add(new Label { Text=title,AutoSize=true,Font=new Font("Segoe UI",11,FontStyle.Bold),Margin=new Padding(0,0,0,14) });
        Add(parent,card);return card;
    }
    public static void Apply(Control root)
    {
        root.ForeColor=root.Tag as string=="muted" ? Muted : Text;
        root.BackColor=root is Form ? Background : root.Parent?.BackColor??Background;
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
    private bool hovered,pressed;
    public bool Primary { get; set; }
    public bool Selected { get; set; }
    public bool Navigation { get; set; }
    public BridgeButton() { AutoSize=true;MinimumSize=new Size(100,42);Margin=new Padding(0,0,10,8);Padding=new Padding(14,8,14,8);FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true); }
    protected override void OnMouseEnter(EventArgs e) { hovered=true;base.OnMouseEnter(e);Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered=pressed=false;base.OnMouseLeave(e);Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed=e.Button==MouseButtons.Left;base.OnMouseDown(e);Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed=false;base.OnMouseUp(e);Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor??BridgeTheme.Background);
        Color fill=Primary && Enabled?BridgeTheme.Blue:Selected?BridgeTheme.Selected:Parent?.BackColor??BridgeTheme.Surface;
        if(Enabled && (hovered || pressed)) fill=Primary?pressed?Color.FromArgb(37,78,167):Color.FromArgb(65,119,229):BridgeTheme.Selected;
        using GraphicsPath path=BridgeTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),10*DeviceDpi/96f);
        using SolidBrush brush=new(fill);e.Graphics.FillPath(brush,path);
        if(!Primary && !Navigation) { using Pen border=new(BridgeTheme.Border);e.Graphics.DrawPath(border,path); }
        Rectangle textBounds=Navigation?Rectangle.Inflate(ClientRectangle,-14,0):ClientRectangle;
        TextRenderer.DrawText(e.Graphics,Text,Font,textBounds,!Enabled?BridgeTheme.Muted:Primary?Color.White:Selected?BridgeTheme.Link:BridgeTheme.Text,
            (Navigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),Primary?Color.White:BridgeTheme.Link,fill);
    }
}

internal sealed class BridgeCard : FlowLayoutPanel
{
    public BridgeCard()
    {
        AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FlowDirection=FlowDirection.TopDown;WrapContents=false;
        Padding=new Padding(20);Margin=new Padding(0,4,0,12);Dock=DockStyle.Fill;DoubleBuffered=true;
        SizeChanged+=(_,_)=>Fit();ControlAdded+=(_,_)=>Fit();
    }
    private void Fit()
    {
        int available=Math.Max(150,ClientSize.Width-Padding.Horizontal);
        foreach(Control child in Controls) {
            if(child is Label or FlowLayoutPanel or Button or CheckBox) child.MaximumSize=new Size(available,0);
            if(child is FlowLayoutPanel row) foreach(Control item in row.Controls)
                if(item is Button or CheckBox or Label) item.MaximumSize=new Size(Math.Max(100,available-item.Margin.Horizontal),0);
            if(child is DeviceMap or TextBox || child is Label { AutoSize:false }) child.Width=available;
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent?.BackColor??BridgeTheme.Background);
        using GraphicsPath path=BridgeTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),12*DeviceDpi/96f);
        using SolidBrush fill=new(BridgeTheme.Surface);e.Graphics.FillPath(fill,path);
    }
}
