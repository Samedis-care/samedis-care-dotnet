using FluentAssertions;
using SamedisCare.Api.Logging;
using Xunit;

namespace SamedisCare.Api.Tests.Logging;

// FileSyncLog consolidates the per-tool Helper.Message copies. Level gating and the log
// mode are the parts a tool's config.yml drives, so those are what these tests pin.
public class FileSyncLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"synclog_{Guid.NewGuid():N}");

    private string LogPath => Path.Combine(_dir, "sub", "sync.log");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string[] Lines() => File.Exists(LogPath) ? File.ReadAllLines(LogPath) : Array.Empty<string>();

    [Fact]
    public void Creates_missing_directories_on_first_write()
    {
        new FileSyncLog(1, LogMode.File, LogPath).Info("hello");

        File.Exists(LogPath).Should().BeTrue();
        Lines().Should().ContainSingle().Which.Should().Contain("INFO").And.Contain("hello");
    }

    [Fact]
    public void Debug_is_suppressed_below_level_two()
    {
        var log = new FileSyncLog(1, LogMode.File, LogPath);
        log.Info("in");
        log.Debug("out");

        Lines().Should().ContainSingle().Which.Should().Contain("in");
    }

    [Fact]
    public void Debug_is_written_at_level_two()
    {
        new FileSyncLog(2, LogMode.File, LogPath).Debug("visible");

        Lines().Should().ContainSingle().Which.Should().Contain("DEBUG").And.Contain("visible");
    }

    [Fact]
    public void Level_zero_suppresses_everything_including_errors()
    {
        var log = new FileSyncLog(0, LogMode.File, LogPath);
        log.Info("a");
        log.Warn("b");
        log.Error("c");
        log.Debug("d");

        Lines().Should().BeEmpty();
    }

    [Fact]
    public void Mode_none_writes_no_file_even_at_debug_level()
    {
        new FileSyncLog(2, LogMode.None, LogPath).Info("nope");

        File.Exists(LogPath).Should().BeFalse();
    }

    [Fact]
    public void Mode_console_writes_no_file()
    {
        new FileSyncLog(2, LogMode.Console, LogPath).Info("console only");

        File.Exists(LogPath).Should().BeFalse();
    }

    [Fact]
    public void Messages_are_appended_not_overwritten()
    {
        var log = new FileSyncLog(1, LogMode.File, LogPath);
        log.Info("first");
        log.Warn("second");

        var lines = Lines();
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("first");
        lines[1].Should().Contain("WARN").And.Contain("second");
    }

    [Fact]
    public void An_exception_is_appended_to_the_error_message()
    {
        new FileSyncLog(1, LogMode.File, LogPath)
            .Error("upload failed", new InvalidOperationException("boom"));

        Lines().Should().ContainSingle().Which
               .Should().Contain("ERROR").And.Contain("upload failed").And.Contain("boom");
    }

    [Fact]
    public void Level_is_exposed_so_callers_can_skip_expensive_debug_work()
        => new FileSyncLog(2, LogMode.None, LogPath).Level.Should().Be(2);
}
