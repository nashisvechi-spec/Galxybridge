namespace GalaxyBridge.Windows;

internal static class AppIcon
{
    // Embedded data is available in a single-file publish without external files.
    public static Icon Value { get; } = Load();

    private static Icon Load()
    {
        using Stream data = typeof(AppIcon).Assembly.GetManifestResourceStream("GalaxyBridge.App.ico")
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using Icon source = new(data);
        return (Icon)source.Clone();
    }
}
