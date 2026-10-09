namespace GalaxyBridge.Windows;

// The linked production identity must never read/write the user's real profile.
internal static class Settings
{
    public static string Folder { get; } = Path.Combine(Path.GetTempPath(), "GalaxyBridge-TlsTest-" + Guid.NewGuid().ToString("N"));
}
