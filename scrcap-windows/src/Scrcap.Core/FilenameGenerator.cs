using System.Globalization;
using System.Text;

namespace Scrcap.Core;

public static class FilenameGenerator
{
    public static string Filename(string pattern, DateTimeOffset? now = null)
    {
        var instant = now ?? DateTimeOffset.Now;
        var expanded = pattern
            .Replace("{date}", instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{time}", instant.ToString("HH.mm.ss", CultureInfo.InvariantCulture), StringComparison.Ordinal);

        return SafeFilenameStem(expanded) + ".png";
    }

    public static string SafeFilenameStem(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var builder = new StringBuilder(raw.Length);
        var previousWasSeparator = false;

        foreach (var ch in raw)
        {
            if (IsInvalidFilenameCharacter(ch))
            {
                if (!previousWasSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                    previousWasSeparator = true;
                }

                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
                previousWasSeparator = false;
                continue;
            }

            builder.Append(ch);
            previousWasSeparator = false;
        }

        var stem = builder.ToString().Trim(' ', '-', '\t', '\r', '\n').TrimEnd(' ', '.');
        if (stem.Length == 0)
        {
            return "scrcap";
        }

        return IsDeviceName(stem) ? "_" + stem : stem;
    }

    private static bool IsDeviceName(string stem)
    {
        var dot = stem.IndexOf('.');
        var name = (dot < 0 ? stem : stem[..dot]).TrimEnd(' ');
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Length == 4
            && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && name[3] is >= '1' and <= '9' or '¹' or '²' or '³';
    }

    private static bool IsInvalidFilenameCharacter(char ch) =>
        ch is '/' or ':' or '\\' or '"' or '<' or '>' or '|' or '?' or '*'
        || char.IsControl(ch);
}
