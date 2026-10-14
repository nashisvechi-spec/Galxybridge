using Microsoft.Win32;

namespace GalaxyBridge.Windows;

internal static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled => string.Equals(Registry.GetValue(@"HKEY_CURRENT_USER\" + Key, "GalaxyBridge", null) as string, Command, StringComparison.OrdinalIgnoreCase);
    private static string Command => "\"" + Application.ExecutablePath + "\" --minimized";
    public static void Set(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) key.SetValue("GalaxyBridge", Command, RegistryValueKind.String);
        else if (key.GetValue("GalaxyBridge") is string current && string.Equals(current,Command,StringComparison.OrdinalIgnoreCase)) key.DeleteValue("GalaxyBridge", false);
    }
}
