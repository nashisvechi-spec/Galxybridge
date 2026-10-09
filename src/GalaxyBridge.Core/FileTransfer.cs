using System.Globalization;
using System.Text;

namespace GalaxyBridge.Core;

public static class FileTransfer
{
    public const string Root = "/sdcard/Download/GalaxyBridge";

    public static string DirectoryName(DateTimeOffset time, string nonce)
    {
        if (nonce.Length != 32 || !nonce.All(c => char.IsAsciiHexDigit(c)))
            throw new FormatException("Неверный идентификатор передачи.");
        return Root + "/" + time.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + nonce;
    }

    public static string[] AllocateNames(IEnumerable<string> names)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string name in names)
        {
            ValidateName(name);
            string candidate = name;
            int dot = name.LastIndexOf('.');
            string stem = dot > 0 ? name[..dot] : name, extension = dot > 0 ? name[dot..] : "";
            for (int suffix = 2; !used.Add(candidate); suffix++)
            {
                candidate = stem + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")" + extension;
                ValidateName(candidate);
            }
            result.Add(candidate);
        }
        return result.ToArray();
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.Any(c => c is '/' or '\\' || char.IsControl(c)) || Encoding.UTF8.GetByteCount(name) > 255)
            throw new FormatException("Имя файла не подходит для телефона. Укоротите его или уберите специальные символы.");
    }

    // ProcessStartInfo avoids the local shell; adb shell still needs remote quoting.
    public static string ShellQuote(string value)
    {
        if (value.Any(c => c == '\0' || c is '\r' or '\n'))
            throw new FormatException("Недопустимый путь для передачи.");
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}
