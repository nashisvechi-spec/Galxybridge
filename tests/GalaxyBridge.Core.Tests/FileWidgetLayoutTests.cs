using System.Drawing;
using GalaxyBridge.Windows;

internal static class FileWidgetLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        foreach (Size size in new[] { new Size(320,184), new Size(480,276), new Size(640,368), new Size(240,138) })
        {
            Point At(int x, int y) => new((int)Math.Round(x * size.Width / (double)FileWidgetLayout.ArtWidth),
                (int)Math.Round(y * size.Height / (double)FileWidgetLayout.ArtHeight));
            FileWidgetAction Hit(int x, int y, bool busy = false) => FileWidgetLayout.Hit(At(x, y), size, busy);
            check(Hit(292,26) == FileWidgetAction.Close, $"widget close center at {size}");
            check(Hit(283,34) == FileWidgetAction.Close, $"widget close inner edge at {size}");
            check(Hit(301,34) == FileWidgetAction.Close, $"widget close outer edge at {size}");
            check(Hit(160,89) == FileWidgetAction.Send && Hit(160,116) == FileWidgetAction.Send, $"both widget arrows choose files at {size}");
            check(Hit(274,150,true) == FileWidgetAction.Cancel, $"busy cancel cannot trigger send at {size}");
            check(Hit(274,150) == FileWidgetAction.Send, $"idle has no hidden cancel target at {size}");
            check(Hit(160,26) == FileWidgetAction.Move && Hit(160,178) == FileWidgetAction.Move, $"top/bottom frame moves without sending at {size}");
            check(Hit(5,100) == FileWidgetAction.Move && Hit(315,100) == FileWidgetAction.Move, $"side frame moves without sending at {size}");
            check(FileWidgetLayout.Hit(new Point(-1,1),size,false) == FileWidgetAction.None &&
                FileWidgetLayout.Hit(new Point(size.Width,1),size,false) == FileWidgetAction.None, "outside widget has no action");
        }
        check(FileWidgetLayout.Hit(Point.Empty,Size.Empty,false) == FileWidgetAction.None, "zero-sized widget has no action");
        string directory = Path.Combine(Path.GetTempPath(), "gb-widget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory,"test.txt");File.WriteAllText(file,"widget");
            check(FileWidgetLayout.CanDrop(true,false,[file]), "connected idle widget accepts file");
            check(!FileWidgetLayout.CanDrop(false,false,[file]), "disconnected widget rejects file");
            check(!FileWidgetLayout.CanDrop(true,true,[file]), "busy widget rejects second transfer");
            check(!FileWidgetLayout.CanDrop(true,false,[directory]), "widget rejects folders");
            check(!FileWidgetLayout.CanDrop(true,false,[file,Path.Combine(directory,"missing")]), "widget rejects partially missing batch");
            check(!FileWidgetLayout.CanDrop(true,false,null) && !FileWidgetLayout.CanDrop(true,false,[]), "widget rejects missing/empty data");
            check(FileWidgetLayout.CanDrop(true,false,Enumerable.Repeat(file,100).ToArray()), "widget accepts 100-file batch");
            check(!FileWidgetLayout.CanDrop(true,false,Enumerable.Repeat(file,101).ToArray()), "widget rejects 101-file batch");
        }
        finally { Directory.Delete(directory,true); }
    }
}
