using System.Drawing.Drawing2D;
using GalaxyBridge.Core;
namespace GalaxyBridge.Windows;

internal sealed class DeviceMap : Control
{
    public PhoneSide Side { get; set; }
    public bool Connected { get; set; }
    public bool Desktop { get; set; }
    public DeviceMap() { Height=190;Width=550;Margin=new Padding(0,0,0,16);DoubleBuffered=true;AccessibleName="Расположение компьютера и телефона"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BridgeTheme.Surface);
        using(GraphicsPath background=BridgeTheme.Round(new RectangleF(0,0,Width,Height),12*DeviceDpi/96f))
        using(SolidBrush fill=new(BridgeTheme.Background)) g.FillPath(fill,background);
        float scale=Math.Min(Width/550f,Height/190f),cx=Width/2f,cy=Height/2f;
        float px=Side==PhoneSide.Left?cx-135*scale:Side==PhoneSide.Right?cx+135*scale:cx;
        float py=Side==PhoneSide.Top?cy-62*scale:Side==PhoneSide.Bottom?cy+62*scale:cy;
        float lx=Side==PhoneSide.Left?cx+55*scale:Side==PhoneSide.Right?cx-55*scale:cx;
        float ly=Side==PhoneSide.Top?cy+40*scale:Side==PhoneSide.Bottom?cy-40*scale:cy;
        using Pen line=new(Connected?BridgeTheme.Link:BridgeTheme.Muted,2*scale) { DashStyle=Connected?DashStyle.Solid:DashStyle.Dash };
        g.DrawLine(line,lx,ly,px,py);
        DrawDevice(g,new RectangleF(lx-68*scale,ly-40*scale,136*scale,80*scale),"ПК",false);
        DrawDevice(g,new RectangleF(px-23*scale,py-43*scale,46*scale,86*scale),Desktop?"DeX":"S25",true);
        TextRenderer.DrawText(g,Connected?"Связь установлена":"Ожидаем подключение",Font,new Rectangle(0,Height-(int)(28*DeviceDpi/96f),Width,(int)(26*DeviceDpi/96f)),Connected?BridgeTheme.Link:BridgeTheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
    private static void DrawDevice(Graphics g,RectangleF rect,string label,bool phone)
    {
        using GraphicsPath path=BridgeTheme.Round(rect,phone?9:6);using SolidBrush body=new(BridgeTheme.Surface);using Pen edge=new(BridgeTheme.Muted,2);
        g.FillPath(body,path);g.DrawPath(edge,path);
        RectangleF screen=RectangleF.Inflate(rect,-6,-8);
        using SolidBrush fill=new(BridgeTheme.Selected);g.FillRectangle(fill,screen);
        using Font font=new("Segoe UI",phone?8:13,FontStyle.Bold);
        TextRenderer.DrawText(g,label,font,Rectangle.Round(screen),BridgeTheme.Link,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        if(!phone) g.DrawLine(edge,rect.Left-8,rect.Bottom+5,rect.Right+8,rect.Bottom+5);
    }
}
