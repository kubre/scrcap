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

        var stem = builder.ToString().Trim(' ', '-', '\t', '\r', '\n');
        return stem.Length == 0 ? "scrcap" : stem;
    }

    private static bool IsInvalidFilenameCharacter(char ch) =>
        ch is '/' or ':' or '\\' or '"' or '<' or '>' or '|' or '?' or '*'
        || char.IsControl(ch);
}
