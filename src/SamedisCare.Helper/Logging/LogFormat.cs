using System.Globalization;
using System.Text.RegularExpressions;

namespace SamedisCare.Helper.Logging;

/// <summary>One parsed line of a sync log.</summary>
/// <param name="At">When it was written.</param>
/// <param name="Level">The level token, e.g. <c>ERROR</c>. Not validated -- see the remarks
/// on <see cref="LogFormat.TryParse"/>.</param>
/// <param name="Message">Everything after the level.</param>
public readonly record struct LogEntry(DateTime At, string Level, string Message);

/// <summary>
/// The shape of a line in a sync log, written down where both sides can see it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FileSyncLog"/> writes these lines and samedis-care-log-monitor reads them, from
/// a different repository. The format used to live in two places: a const in the writer and a
/// regular expression in the reader, with nothing tying them together and no test over the
/// round trip.
/// </para>
/// <para>
/// That is worse than it sounds, because of how the reader fails. A line it cannot match is
/// not an error to it -- it is treated as the continuation of the previous entry. Change the
/// format on the writing side and the monitor reports nothing at all: no parse error, no
/// warning, just a log in which every ERROR has been folded into the text of whatever came
/// before it. A monitor that has gone blind looks exactly like a run with no problems.
/// </para>
/// </remarks>
public static class LogFormat
{
    /// <summary>Timestamp at the start of every line.</summary>
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Date in a log file's name. ISO on purpose: the tools used to build the name with
    /// <c>ToShortDateString()</c>, which follows the machine's culture, so the same tool
    /// produced <c>Logfile_30.08.2026.log</c> on one host and <c>Logfile_2026-08-30.log</c>
    /// on the next -- and the monitor had to carry six candidate formats to find either.
    /// </summary>
    public const string FileNameDateFormat = "yyyy-MM-dd";

    /// <summary>The levels <see cref="FileSyncLog"/> writes.</summary>
    public static class Levels
    {
        public const string Info = "INFO";
        public const string Warn = "WARN";
        public const string Error = "ERROR";
        public const string Debug = "DEBUG";

        /// <summary>All four, for a reader that wants to filter.</summary>
        public static readonly IReadOnlyList<string> All = new[] { Info, Warn, Error, Debug };
    }

    // The level is \S+ rather than one of the four known names, so that a line written by
    // something else still parses as an entry instead of silently joining the one above it.
    // Deciding whether a level is one it cares about is the reader's business -- see Levels.
    private static readonly Regex Line = new(
        @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s+(\S+)\s?(.*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Builds the line <see cref="FileSyncLog"/> appends to the file.</summary>
    public static string Compose(DateTime at, string level, string message)
        => $"{at.ToString(TimeFormat, CultureInfo.InvariantCulture)} {level} {message}";

    /// <summary>
    /// Reads a line back. False for anything that is not an entry -- a blank line, a stack
    /// trace, a JSON body: all of those belong to the entry above.
    /// </summary>
    /// <remarks>
    /// The level is returned as written and not checked against <see cref="Levels"/>. A
    /// stricter parse would turn a line carrying an unknown level into a continuation of the
    /// previous entry, which is the one outcome worth avoiding: it hides the line instead of
    /// reporting it.
    /// </remarks>
    public static bool TryParse(string? line, out LogEntry entry)
    {
        entry = default;
        if (string.IsNullOrEmpty(line)) return false;

        var m = Line.Match(line);
        if (!m.Success) return false;

        if (!DateTime.TryParseExact(m.Groups[1].Value, TimeFormat, CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var at))
            return false;

        entry = new LogEntry(at, m.Groups[2].Value, m.Groups[3].Value);
        return true;
    }

    /// <summary>The name of a day's log file, e.g. <c>Logfile_2026-08-30.log</c>.</summary>
    public static string FileName(DateTime at, string prefix = "Logfile_", string extension = ".log")
        => $"{prefix}{at.ToString(FileNameDateFormat, CultureInfo.InvariantCulture)}{extension}";

    /// <summary>
    /// The date in a log file's name, from a path or a bare name. False when there is none.
    /// </summary>
    public static bool TryParseFileName(string? path, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(path)) return false;

        var name = Path.GetFileNameWithoutExtension(path);
        var underscore = name.LastIndexOf('_');
        var candidate = underscore >= 0 ? name[(underscore + 1)..] : name;

        return DateTime.TryParseExact(candidate, FileNameDateFormat, CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out date);
    }
}
