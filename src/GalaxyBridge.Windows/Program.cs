namespace GalaxyBridge.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using Mutex instance = new(true, "GalaxyBridge.CurrentUser." + Environment.UserName, out bool owner);
        if (!owner && Environment.GetCommandLineArgs().Contains("--minimized",StringComparer.OrdinalIgnoreCase)) return;
        if (!owner) { MessageBox.Show("Galaxy Bridge уже запущен. Откройте его из области уведомлений."); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        GalaxyBridge.Core.LanDragStaging.PruneCache();
        try { Application.Run(new LanForm(Settings.Load())); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { MessageBox.Show("Не удалось запустить Galaxy Bridge: " + ex.Message, "Galaxy Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
