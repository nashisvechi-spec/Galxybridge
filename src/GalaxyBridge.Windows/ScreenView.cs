using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed class ScreenView : Form
{
    private sealed class Canvas : Control
    {
        public Image? Image;
        public Action<double,double,double,double,int,int>? Gesture;
        public Action<string,int>? TextInput,KeyInput;
        private char? surrogate;
        public int Revision;
        private Point? down;
        private long downAt;
        private int downRevision;
        public Canvas() { DoubleBuffered=true;BackColor=Color.FromArgb(13,20,34);SetStyle(ControlStyles.Selectable|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);TabStop=true; }
        private Rectangle ImageArea {
            get { if(Image is null) return Rectangle.Empty;double scale=Math.Min(ClientSize.Width/(double)Image.Width,ClientSize.Height/(double)Image.Height);int w=(int)(Image.Width*scale),h=(int)(Image.Height*scale);return new((ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h); }
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if(Image is not null) e.Graphics.DrawImage(Image,ImageArea);
            else TextRenderer.DrawText(e.Graphics,"Ожидаем изображение телефона…",Font,ClientRectangle,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
        }
        private (double X,double Y)? Map(Point point,bool clamp=false) {
            Rectangle area=ImageArea;if(area.Width<=1 || area.Height<=1 || !clamp && !area.Contains(point)) return null;
            return (Math.Clamp((point.X-area.Left)/(double)(area.Width-1),0,1),Math.Clamp((point.Y-area.Top)/(double)(area.Height-1),0,1));
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e);Focus();if(e.Button==MouseButtons.Left && Map(e.Location) is not null) { down=e.Location;downRevision=Revision;downAt=Environment.TickCount64;Capture=true; } }
        protected override bool IsInputKey(Keys keyData) => (keyData&Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
        protected override void OnKeyPress(KeyPressEventArgs e) {
            base.OnKeyPress(e);if(char.IsControl(e.KeyChar)) return;
            if(char.IsHighSurrogate(e.KeyChar)) { surrogate=e.KeyChar;e.Handled=true;return; }
            string value=char.IsLowSurrogate(e.KeyChar) && surrogate is char high?new string([high,e.KeyChar]):e.KeyChar.ToString();surrogate=null;
            TextInput?.Invoke(value,Revision);e.Handled=true;
        }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if(e.Control && e.KeyCode==Keys.V) {
                try { if(Clipboard.ContainsText()) TextInput?.Invoke(Clipboard.GetText(),Revision); } catch(System.Runtime.InteropServices.ExternalException) { }
                e.SuppressKeyPress=true;return;
            }
            string? key=e.Control && e.KeyCode==Keys.A?"selectAll":e.KeyCode switch { Keys.Back=>"backspace",Keys.Delete=>"delete",Keys.Enter=>"enter",Keys.Left=>"left",Keys.Right=>"right",Keys.Home=>"home",Keys.End=>"end",_=>null };
            if(key is not null) { KeyInput?.Invoke(key,Revision);e.SuppressKeyPress=true; }
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);if(down is not Point start) return;down=null;Capture=false;if(downRevision!=Revision) return;
            var first=Map(start,true);var last=Map(e.Location,true);if(first is null || last is null) return;
            int duration=Math.Abs(start.X-e.X)+Math.Abs(start.Y-e.Y)<5?70:(int)Math.Clamp(Environment.TickCount64-downAt,100,1000);
            Gesture?.Invoke(first.Value.X,first.Value.Y,last.Value.X,last.Value.Y,duration,Revision);
        }
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);var point=Map(e.Location);if(point is null) return;
            double end=Math.Clamp(point.Value.Y+(e.Delta>0?.22:-.22),.02,.98);
            Gesture?.Invoke(point.Value.X,point.Value.Y,point.Value.X,end,220,Revision);
        }
        protected override void Dispose(bool disposing) { if(disposing) { Image?.Dispose();Image=null; }base.Dispose(disposing); }
    }
    private readonly Canvas canvas=new() { Dock=DockStyle.Fill,AccessibleName="Экран телефона. Нажмите или перетащите мышью для управления." };
    private readonly Label status=new() { Dock=DockStyle.Top,AutoSize=true,MaximumSize=new Size(700,0),Padding=new Padding(12) };
    private ScreenFrame? latest;
    private int scheduled;
    public ScreenView(Action<double,double,double,double,int,int> gesture,Action<string,int> textInput,Action<string,int> keyInput)
    {
        Text="Экран телефона · Galaxy Bridge";Icon=AppIcon.Value;Font=new Font("Segoe UI",10);Size=new Size(520,800);MinimumSize=new Size(300,380);StartPosition=FormStartPosition.CenterParent;
        AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        status.Text="Подтвердите показ экрана на телефоне. Android запрашивает подтверждение для каждого нового сеанса.";
        Resize+=(_,_)=>status.MaximumSize=new Size(Math.Max(100,ClientSize.Width),0);
        canvas.Gesture=gesture;canvas.TextInput=textInput;canvas.KeyInput=keyInput;
        TableLayoutPanel layout=new() { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,Padding=Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        status.Dock=DockStyle.Fill;status.Margin=Padding.Empty;canvas.Margin=Padding.Empty;layout.Controls.Add(status,0,0);layout.Controls.Add(canvas,0,1);Controls.Add(layout);
        Shown+=(_,_)=>BridgeTheme.FitWindow(this);BridgeTheme.Apply(this);
    }
    public void SetStatus(string message) { if(!IsDisposed) status.Text=message; }
    public void Accept(ScreenFrame frame)
    {
        Interlocked.Exchange(ref latest,frame);if(Interlocked.Exchange(ref scheduled,1)!=0) return;
        try { BeginInvoke(new Action(RenderLatest)); } catch(InvalidOperationException) { Interlocked.Exchange(ref scheduled,0); }
    }
    private void RenderLatest()
    {
        ScreenFrame? frame=Interlocked.Exchange(ref latest,null);Interlocked.Exchange(ref scheduled,0);
        if(frame is null || IsDisposed) return;
        try {
            using MemoryStream stream=new(frame.Jpeg,false);using Image decoded=Image.FromStream(stream,true,true);
            if(decoded.Width!=frame.Width || decoded.Height!=frame.Height) { status.Text="Размер полученного кадра не совпадает с изображением. Запросите трансляцию заново.";return; }
            bool first=canvas.Image is null;
            Image? previous=canvas.Image;canvas.Image=new Bitmap(decoded);canvas.Revision=frame.Revision;previous?.Dispose();canvas.Invalidate();
            if(first) status.Text=$"Экран телефона · {frame.Width}×{frame.Height}. Нажмите на изображение для управления.";
        } catch(Exception e) when(e is ArgumentException or OutOfMemoryException) { status.Text="Кадр не удалось показать. Запросите трансляцию заново."; }
    }
    protected override void Dispose(bool disposing) { Interlocked.Exchange(ref latest,null);base.Dispose(disposing); }
}
