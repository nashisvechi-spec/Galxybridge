using System.Text.Json;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed class Settings
{
    public PhoneSide PhoneSide { get; set; } = PhoneSide.Right;
    public double Sensitivity { get; set; } = 1.0;
    public bool ClipboardEnabled { get; set; } = true;
    public bool EdgeEntryEnabled { get; set; } = true;
    public int EdgeEntryDelayMs { get; set; } = 350;
    public bool FileWidgetVisible { get; set; }
    private int fileWidgetTransparency;
    public int FileWidgetTransparency
    {
        get => fileWidgetTransparency;
        set => fileWidgetTransparency = Math.Clamp(value, 0, 80);
    }
    public int? FileWidgetX { get; set; }
    public int? FileWidgetY { get; set; }
    public bool WheelZoomEnabled { get; set; }
    public string ThemeMode { get; set; } = "system";
    public bool NativeReceiveEnabled { get; set; } = true;
    public string NativeReceiveFolder { get; set; } = DefaultReceiveFolder;
    public static string DefaultReceiveFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "GalaxyBridge");
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalaxyBridge");
    private static string PathName => Path.Combine(Folder, "settings.json");
    public static Settings Load()
    {
        try
        {
            Settings result = JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathName)) ?? new();
            if (!Enum.IsDefined(result.PhoneSide)) result.PhoneSide = PhoneSide.Right;
            if (result.ThemeMode is not ("system" or "light" or "dark")) result.ThemeMode = "system";
            result.Sensitivity = double.IsFinite(result.Sensitivity) ? Math.Clamp(result.Sensitivity, .25, 4) : 1;
            result.EdgeEntryDelayMs = Math.Clamp(result.EdgeEntryDelayMs, 0, 2000);
            if (string.IsNullOrWhiteSpace(result.NativeReceiveFolder) || !Path.IsPathFullyQualified(result.NativeReceiveFolder)) result.NativeReceiveFolder = DefaultReceiveFolder;
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        string temp = PathName + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, PathName, overwrite: true);
    }
}
