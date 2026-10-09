using System.Globalization;
using System.Text;

namespace GalaxyBridge.Core;

public static class FileTransfer
{
    public const string Root = "/sdcard/Download/GalaxyBridge";

    public static string[] AllocateNames(IEnumerable<string> names, IEnumerable<string>? existing = null)
    {
        HashSet<string> used = new(existing ?? [], StringComparer.OrdinalIgnoreCase);
        List<string> result = [];
        foreach (string name in names)
        {
            ValidateName(name);
            string candidate = name;
            int dot = name.LastIndexOf('.');
            string stem = dot > 0 ? name[..dot] : name, extension = dot > 0 ? name[dot..] : "";
            for (int suffix = 2; !used.Add(candidate); suffix++)
            {
                string tail = " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")" + extension;
                int remaining = 255 - Encoding.UTF8.GetByteCount(tail);
                if (remaining < 1) throw new FormatException("Укоротите расширение файла для добавления суффикса.");
                StringBuilder shortened = new();
                foreach (Rune rune in stem.EnumerateRunes())
                {
                    if (rune.Utf8SequenceLength > remaining) break;
                    shortened.Append(rune.ToString()); remaining -= rune.Utf8SequenceLength;
                }
                candidate = shortened.ToString() + tail;
                ValidateName(candidate);
            }
            result.Add(candidate);
        }
        return result.ToArray();
    }

    public static string[] ParseExistingNames(string output) => output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.StartsWith(Root + "/", StringComparison.Ordinal))
        .Select(path => path[(Root.Length + 1)..]).Where(name => name.Length > 0 && !name.Contains('/')).ToArray();

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
