using FluentAssertions;
using SamedisCare.Helper.Logging;
using Xunit;

namespace SamedisCare.Helper.Tests;

/// <summary>
/// The writer of a sync log lives here and its reader lives in samedis-care-log-monitor, in
/// another repository. These tests are the seam between them: they fail here if the format
/// moves, which is the only place it can be noticed. The monitor cannot notice -- a line it
/// does not recognise is not an error to it, it is the continuation of the previous entry,
/// so a format change makes it fold every ERROR into the text above and report nothing.
/// </summary>
public class LogFormatTests
{
    private static readonly DateTime At = new(2026, 8, 30, 14, 53, 38);

    [Theory]
    [InlineData("INFO")]
    [InlineData("WARN")]
    [InlineData("ERROR")]
    [InlineData("DEBUG")]
    public void Every_level_survives_the_round_trip(string level)
    {
        LogFormat.TryParse(LogFormat.Compose(At, level, "Inventories Upload finished."), out var entry)
                 .Should().BeTrue();

        entry.Should().Be(new LogEntry(At, level, "Inventories Upload finished."));
    }

    [Fact]
    public void The_line_starts_with_an_iso_timestamp_and_the_level()
        => LogFormat.Compose(At, "WARN", "Skipped inventory row.")
                    .Should().Be("2026-08-30 14:53:38 WARN Skipped inventory row.");

    // A message may carry anything -- quotes, semicolons, the separator the console prints.
    // Only the first two tokens are structure.
    [Theory]
    [InlineData("device 'X' / 'Y' (catalog='') not resolvable")]
    [InlineData("2026-08-30 14:53:38 looks like a timestamp but is not one")]
    [InlineData("****************")]
    [InlineData("")]
    public void The_message_is_returned_as_written(string message)
    {
        LogFormat.TryParse(LogFormat.Compose(At, "INFO", message), out var entry).Should().BeTrue();

        entry.Message.Should().Be(message);
    }

    // What the reader relies on to tell an entry from a stack trace.
    [Theory]
    [InlineData("   at System.RuntimeMethodHandle.InvokeMethod(...)")]
    [InlineData("{\"meta\":{\"msg\":{\"error\":\"x\"}}}")]
    [InlineData("")]
    [InlineData("30.08.2026 14:53:38 INFO culture-dependent stamp")]
    [InlineData("2026-13-45 99:99:99 INFO impossible date")]
    public void Anything_that_is_not_an_entry_is_rejected(string line)
        => LogFormat.TryParse(line, out _).Should().BeFalse();

    // Deliberately permissive: a line written by something else must still parse as an entry.
    // Rejecting it would hide it inside the previous one, which is the outcome to avoid.
    [Fact]
    public void An_unfamiliar_level_still_parses_as_an_entry()
    {
        LogFormat.TryParse("2026-08-30 14:53:38 TRACE from another writer", out var entry)
                 .Should().BeTrue();

        entry.Level.Should().Be("TRACE");
        LogFormat.Levels.All.Should().NotContain("TRACE", "deciding what to do with it is the reader's business");
    }

    [Fact]
    public void The_file_name_carries_an_iso_date()
        => LogFormat.FileName(At).Should().Be("Logfile_2026-08-30.log");

    [Theory]
    [InlineData("Logfile_2026-08-30.log")]
    [InlineData("log/Logfile_2026-08-30.log")]
    [InlineData("/var/sync/log/Logfile_2026-08-30.log")]
    public void The_date_is_read_back_out_of_the_name(string path)
    {
        LogFormat.TryParseFileName(path, out var date).Should().BeTrue();
        date.Should().Be(new DateTime(2026, 8, 30));
    }

    // The culture-dependent name the tools used to produce. Read back it is not a date, which
    // is why the monitor carried six candidate formats instead of one.
    [Theory]
    [InlineData("Logfile_30.08.2026.log")]
    [InlineData("Logfile.log")]
    [InlineData("")]
    public void A_name_without_an_iso_date_is_not_a_date(string path)
        => LogFormat.TryParseFileName(path, out _).Should().BeFalse();
}

/// <summary>
/// The writer against the format, so the two cannot drift: these read back what
/// <see cref="FileSyncLog"/> actually put on disk.
/// </summary>
public class FileSyncLogFormatTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"logformat-{Guid.NewGuid():N}.log");

    public void Dispose() => File.Delete(_path);

    private string[] Lines() => File.ReadAllLines(_path);

    [Fact]
    public void What_the_writer_puts_on_disk_is_what_the_parser_reads()
    {
        var log = new FileSyncLog(2, LogMode.File, _path);

        log.Info("Sync started.");
        log.Warn("Skipped inventory row.");
        log.Error("Create failed", new InvalidOperationException("boom"));
        log.Debug("Resolved catalog_id.");

        var levels = new List<string>();
        var messages = new List<string>();
        foreach (var line in Lines())
        {
            LogFormat.TryParse(line, out var entry).Should().BeTrue($"'{line}' was written by FileSyncLog");
            levels.Add(entry.Level);
            messages.Add(entry.Message);
        }

        levels.Should().Equal("INFO", "WARN", "ERROR", "DEBUG");
        messages[2].Should().Be("Create failed: boom", "the exception text belongs to the message");
    }

    [Fact]
    public void A_message_spanning_several_lines_keeps_its_first_line_parseable()
    {
        new FileSyncLog(1, LogMode.File, _path)
            .Error("Import failed\n   at Program.Main()\n   at Runner.Run()");

        var lines = Lines();
        LogFormat.TryParse(lines[0], out var first).Should().BeTrue();
        first.Message.Should().Be("Import failed");

        LogFormat.TryParse(lines[1], out _).Should().BeFalse(
            "the continuation belongs to the entry above and must not look like a new one");
    }
}
