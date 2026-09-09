using System.Globalization;
namespace SamedisCare.Helper.Text;

/// <summary>
/// Small string and file-name helpers that appeared in more than one sync tool.
/// </summary>
public static class Strings
{
    /// <summary>
    /// Characters replaced by <see cref="SanitizeFileName"/>: the running platform's
    /// invalid set, plus the Windows set unconditionally.
    /// <para>
    /// The union is deliberate. <see cref="Path.GetInvalidFileNameChars"/> returns only
    /// <c>/</c> and NUL on Linux and macOS, so a name sanitized there can keep a
    /// backslash, a colon or a pipe — all invalid on Windows. These tools are built on
    /// macOS and in Linux CI but published for win-x64, so sanitizing against the build
    /// machine's rules would produce names the target platform rejects.
    /// </para>
    /// </summary>
    private static readonly char[] InvalidFileNameChars =
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' })
            .Distinct()
            .ToArray();

    /// <summary>
    /// Makes a value safe to use as a file name: characters no supported platform allows
    /// become underscores, and so do spaces. Returns an empty string for null or empty
    /// input. The result is the same regardless of the operating system it runs on.
    /// <para>
    /// Spaces are replaced too — not required by any file system, but the tools put these
    /// names into log lines and CSV columns where an unquoted space is inconvenient. Kept
    /// from the previous behaviour.
    /// </para>
    /// </summary>
    public static string SanitizeFileName(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sanitized = new string(value.Select(c => InvalidFileNameChars.Contains(c) ? '_' : c).ToArray());
        return sanitized.Replace(" ", "_");
    }

    /// <summary>
    /// Returns the first value that is neither null, empty nor whitespace, or null when
    /// there is none. Useful for reading a field that a source system may deliver under
    /// one of several column names.
    /// </summary>
    public static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Culture-invariant integer parse.</summary>
    public static bool TryParseInt(string? value, out int parsed)
        => int.TryParse(value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out parsed);

    /// <summary>
    /// Parses an integer, returning <paramref name="fallback"/> for null, empty or
    /// unparseable input. Same parse as <see cref="TryParseInt"/>, different shape.
    /// </summary>
    public static int ParseIntOrDefault(string? value, int fallback = 0)
        => TryParseInt(value, out var parsed) ? parsed : fallback;

    /// <summary>
    /// Boolean parse accepting <c>true/false</c>, <c>yes/no</c>, <c>ja/nein</c> and
    /// <c>1/0</c>, case-insensitive. The German forms are there because the import
    /// sources are German exports. Returns false when the value is not recognised, which
    /// a caller must distinguish from a parsed <c>false</c>.
    /// </summary>
    /// <summary>
    /// Culture-invariant long parse, for identifiers and counters that arrive as text.
    /// Invariant rather than configured, because a group separator has no business in an id.
    /// </summary>
    public static bool TryParseLong(string? value, out long parsed)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    public static bool TryParseBool(string? value, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(value)) return false;
        switch (value.Trim().ToLowerInvariant())
        {
            // "y"/"n" come from external-sync's source exports; the rest were already here.
            case "true": case "yes": case "y": case "ja": case "1":
                parsed = true; return true;
            case "false": case "no": case "n": case "nein": case "0":
                parsed = false; return true;
            default:
                return false;
        }
    }
}
