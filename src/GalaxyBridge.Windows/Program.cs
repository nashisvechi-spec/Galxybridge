namespace GalaxyBridge.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using Mutex instance = new(true, "GalaxyBridge.CurrentUser." + Environment.UserName, out bool owner);
        if (!owner) { MessageBox.Show("Galaxy Bridge уже запущен. Откройте его из области уведомлений."); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
