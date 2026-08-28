using FluentAssertions;
using Xunit;
using SamedisCare.Api.Http;

namespace SamedisCare.Api.Tests;

// The diagnostic GET dump came over from staff-sync, where it was untested. Its whole job
// is to keep one request on one CSV row, so quoting and truncation are what matter.
public class DebugCsvTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"debug_get_{Guid.NewGuid():N}.csv");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void First_write_creates_the_header_then_appends_rows()
    {
        RequestData.AppendDebugCsvRow(_path, "GET", "/api/v4/tenants/t1/staffs", 200, "{}");
        RequestData.AppendDebugCsvRow(_path, "GET", "/api/v4/tenants/t1/positions", 404, "{}");

        var lines = File.ReadAllLines(_path);

        lines.Should().HaveCount(3);
        lines[0].Should().Be(string.Join(";", RequestData.DebugCsvHeaders));
        lines[1].Should().Contain("/api/v4/tenants/t1/staffs").And.Contain("200");
        lines[2].Should().Contain("/api/v4/tenants/t1/positions").And.Contain("404");
    }

    [Fact]
    public void A_body_with_newlines_stays_on_one_row()
    {
        RequestData.AppendDebugCsvRow(_path, "GET", "/r", 200, "line1\r\nline2\nline3");

        // Header + exactly one data row — the newlines inside the body must not split it.
        File.ReadAllLines(_path).Should().HaveCount(2);
    }

    [Fact]
    public void A_long_body_is_truncated_to_the_preview_length()
    {
        var body = new string('x', RequestData.DebugBodyPreviewLength + 500);

        RequestData.AppendDebugCsvRow(_path, "GET", "/r", 200, body);

        var row = File.ReadAllLines(_path)[1];
        row.Should().Contain(new string('x', RequestData.DebugBodyPreviewLength));
        row.Should().NotContain(new string('x', RequestData.DebugBodyPreviewLength + 1));
    }

    [Fact]
    public void A_null_body_is_written_as_an_empty_field()
    {
        RequestData.AppendDebugCsvRow(_path, "GET", "/r", 500, null);

        File.ReadAllLines(_path)[1].Should().EndWith(";");
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData("has;separator", "\"has;separator\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    public void EscapeCsv_quotes_only_what_needs_quoting(string input, string expected)
        => RequestData.EscapeCsv(input).Should().Be(expected);
}

// LastContent and PostDocument were added for sync-trainings: it reads meta.msg out of a
// failed response after the call, and it uploads to a trainings endpoint rather than an
// issue one. Pinned here so the aliases cannot drift apart.
public class RequestDataSurfaceTests
{
    [Fact]
    public void PostDocument_and_the_issue_aliases_are_the_same_operation()
    {
        var t = typeof(SamedisCare.Api.Http.RequestData);
        var names = new[] { "PostDocument", "PostIssueDocument", "PostIssueImage" };

        foreach (var name in names)
        {
            var m = t.GetMethod(name);
            m.Should().NotBeNull($"{name} must exist");
            m!.GetParameters().Select(p => p.ParameterType).Should().Equal(
                new[] { typeof(string), typeof(string), typeof(string) });
        }
    }

    [Fact]
    public void LastContent_is_exposed_for_after_the_fact_error_parsing()
        => typeof(SamedisCare.Api.Http.RequestData).GetField("LastContent")
             .Should().NotBeNull();
}
