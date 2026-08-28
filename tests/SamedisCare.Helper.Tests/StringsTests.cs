using System.Data;
using FluentAssertions;
using SamedisCare.Helper.Data;
using SamedisCare.Helper.Text;
using Xunit;

namespace SamedisCare.Helper.Tests;

public class StringsTests
{
    [Theory]
    [InlineData("plain.pdf", "plain.pdf")]
    [InlineData("with space.pdf", "with_space.pdf")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeFileName_replaces_invalid_characters_and_spaces(string? input, string expected)
        => Strings.SanitizeFileName(input).Should().Be(expected);

    // These are invalid on Windows but not on Linux or macOS. The tools are built here and
    // published for win-x64, so the result must not depend on the build machine.
    [Theory]
    [InlineData("a/b\\c.pdf", "a_b_c.pdf")]
    [InlineData("C:name.pdf", "C_name.pdf")]
    [InlineData("a|b.pdf", "a_b.pdf")]
    [InlineData("a?b*c.pdf", "a_b_c.pdf")]
    [InlineData("say \"hi\".pdf", "say__hi_.pdf")]
    [InlineData("a<b>c.pdf", "a_b_c.pdf")]
    public void SanitizeFileName_is_platform_independent(string input, string expected)
        => Strings.SanitizeFileName(input).Should().Be(expected);

    [Fact]
    public void FirstNonEmpty_skips_null_empty_and_whitespace()
    {
        Strings.FirstNonEmpty(null, "", "   ", "found", "later").Should().Be("found");
        Strings.FirstNonEmpty(null, "", "  ").Should().BeNull();
        Strings.FirstNonEmpty().Should().BeNull();
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    [InlineData("0", 0)]
    public void TryParseInt_parses_culture_invariantly(string input, int expected)
    {
        Strings.TryParseInt(input, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1,5")]
    public void TryParseInt_rejects_non_integers(string? input)
        => Strings.TryParseInt(input, out _).Should().BeFalse();

    [Fact]
    public void ParseIntOrDefault_falls_back_without_throwing()
    {
        Strings.ParseIntOrDefault("13").Should().Be(13);
        Strings.ParseIntOrDefault("nope", 99).Should().Be(99);
        Strings.ParseIntOrDefault(null).Should().Be(0);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("ja")]
    [InlineData("1")]
    [InlineData(" Ja ")]
    public void TryParseBool_accepts_the_truthy_forms(string input)
    {
        Strings.TryParseBool(input, out var parsed).Should().BeTrue();
        parsed.Should().BeTrue();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("nein")]
    [InlineData("0")]
    public void TryParseBool_accepts_the_falsy_forms(string input)
    {
        Strings.TryParseBool(input, out var parsed).Should().BeTrue();
        parsed.Should().BeFalse();
    }

    // The return value and the out value mean different things: "not recognised" is not
    // the same as "parsed as false", and a caller that conflates them treats junk as no.
    [Theory]
    [InlineData("maybe")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseBool_reports_unrecognised_input_separately(string? input)
    {
        Strings.TryParseBool(input, out var parsed).Should().BeFalse();
        parsed.Should().BeFalse();
    }
}

public class RowsTests
{
    private static DataRow Row()
    {
        var t = new DataTable();
        t.Columns.Add("present");
        t.Columns.Add("nullable");
        var r = t.NewRow();
        r["present"] = "value";
        r["nullable"] = DBNull.Value;
        t.Rows.Add(r);
        return r;
    }

    [Fact]
    public void Value_returns_the_column_content() => Rows.Value(Row(), "present").Should().Be("value");

    // A missing column is normal for these imports — the source file's column set varies —
    // so it must not throw.
    [Fact]
    public void A_missing_column_yields_an_empty_string()
        => Rows.Value(Row(), "absent").Should().BeEmpty();

    [Fact]
    public void A_dbnull_value_yields_an_empty_string()
        => Rows.Value(Row(), "nullable").Should().BeEmpty();
}
