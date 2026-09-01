namespace SamedisCare.Helper.Logging;

/// <summary>
/// Where a <see cref="FileSyncLog"/> writes. The numeric values match the
/// <c>logging.mode</c> setting the sync tools already use in their config.yml, so an
/// existing configuration keeps its meaning.
/// </summary>
public enum LogMode
{
    /// <summary>Discard everything.</summary>
    None = 0,

    /// <summary>Console only.</summary>
    Console = 1,

    /// <summary>File only.</summary>
    File = 2,

    /// <summary>Console and file.</summary>
    Both = 3,
}

/// <summary>
/// Console and/or file logger, consolidating the near-identical <c>Helper.Message</c>
/// implementations that every sync tool carried its own copy of.
/// <para>
/// Format matches the previous behaviour: <c>yyyy-MM-dd HH:mm:ss</c>, a separator line
/// before each console message, and <c>&lt;timestamp&gt; &lt;LEVEL&gt; &lt;message&gt;</c>
/// in the file. Files are appended to, and the directory is created on demand.
/// </para>
/// </summary>
public sealed class FileSyncLog : ISyncLog
{
    private static readonly object FileLock = new();

    private readonly LogMode _mode;
    private readonly string _path;

    public int Level { get; }

    /// <param name="level">0 = off, 1 = info, 2 = debug.</param>
    /// <param name="mode">Console, file, both or none.</param>
    /// <param name="path">
    /// Log file path. Relative paths resolve against the working directory; any directory
    /// in the path is created when first needed. Ignored for
    /// <see cref="LogMode.Console"/> and <see cref="LogMode.None"/>.
    /// </param>
    public FileSyncLog(int level = 1, LogMode mode = LogMode.Both, string path = "log/sync.log")
    {
        Level = level;
        _mode = mode;
        _path = path;
    }

    public void Info(string message) => Write(message, LogFormat.Levels.Info, 1);

    public void Warn(string message) => Write(message, LogFormat.Levels.Warn, 1);

    public void Error(string message, Exception? ex = null)
        => Write(ex == null ? message : $"{message}: {ex.Message}", LogFormat.Levels.Error, 1);

    public void Debug(string message) => Write(message, LogFormat.Levels.Debug, 2);

    private void Write(string message, string type, int requiredLevel)
    {
        if (requiredLevel > Level || _mode == LogMode.None)
            return;

        var at = DateTime.Now;

        if (_mode is LogMode.Console or LogMode.Both)
        {
            Console.WriteLine(new string('*', 80));
            Console.WriteLine($"{at.ToString(LogFormat.TimeFormat)} {message}");
        }

        // Through LogFormat rather than a local template: samedis-care-log-monitor parses
        // these lines, and a format that lives in two places drifts apart without anything
        // noticing -- see the remarks there.
        if (_mode is LogMode.File or LogMode.Both)
            AppendToFile(LogFormat.Compose(at, type, message));
    }

    // Logging must never take the sync down: a full disk or a locked file is not a reason
    // to abort a run, so a write failure is reported on stderr and otherwise swallowed.
    private void AppendToFile(string line)
    {
        try
        {
            lock (FileLock)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(_path, line + "\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[log] could not write to '{_path}': {ex.Message}");
        }
    }
}
