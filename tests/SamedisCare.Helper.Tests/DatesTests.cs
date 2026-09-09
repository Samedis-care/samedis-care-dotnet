using System.Globalization;
using FluentAssertions;
using SamedisCare.Helper;
using Xunit;

namespace SamedisCare.Helper.Tests;

// Dates.TryParse consolidates three parsers that had drifted apart. These tests pin the
// behaviour each of them had, so the consolidation cannot silently change a call site.
public class DatesTests
{
    [Theory]
    [InlineData("2026-08-27")]
    [InlineData("27.08.2026")]
    [InlineData("27.8.2026")]
    [InlineData("  2026-08-27  ")]
    public void Default_formats_cover_iso_and_german(string input)
    {
        Dates.TryParse(input, out var d).Should().BeTrue();
        d.Date.Should().Be(new DateTime(2026, 8, 27));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a date")]
    public void Unparseable_input_returns_false_and_default(string? input)
    {
        Dates.TryParse(input, out var d).Should().BeFalse();
        d.Should().Be(default);
    }

    [Fact]
    public void Two_digit_years_are_accepted()
    {
        Dates.TryParse("27.08.26", out var d).Should().BeTrue();
        d.Date.Month.Should().Be(8);
        d.Date.Day.Should().Be(27);
    }

    [Fact]
    public void Explicit_formats_replace_the_defaults_rather_than_extend_them()
    {
        // Only ISO allowed here, so the German form must fall through to the general
        // parse — which under invariant culture reads 27.08.2026 as no valid date.
        Dates.TryParse("2026-08-27", out _, formats: new[] { "yyyy-MM-dd" }).Should().BeTrue();
        Dates.TryParse("27.08.2026", out _, formats: new[] { "yyyy-MM-dd" }).Should().BeFalse();
    }

    [Fact]
    public void Culture_changes_how_an_ambiguous_date_is_read()
    {
        // 03.04.2026 is 3 April in de-DE and 4 March in en-US.
        Dates.TryParse("03.04.2026", out var de, formats: new[] { "dd.MM.yyyy" },
                       culture: CultureInfo.GetCultureInfo("de-DE")).Should().BeTrue();
        de.Date.Should().Be(new DateTime(2026, 4, 3));

        Dates.TryParse("03/04/2026", out var us, formats: new[] { "MM/dd/yyyy" },
                       culture: CultureInfo.GetCultureInfo("en-US")).Should().BeTrue();
        us.Date.Should().Be(new DateTime(2026, 3, 4));
    }

    // This is the trap the consolidation had to avoid: AssumeLocal and AssumeUniversal are
    // not interchangeable. With AdjustToUniversal a local midnight can land on the
    // previous day, which is exactly how a naive merge would have corrupted dates.
    [Fact]
    public void Styles_are_honoured_and_change_the_resulting_kind()
    {
        Dates.TryParse("2026-08-27T00:00:00", out var local,
                       styles: DateTimeStyles.AssumeLocal).Should().BeTrue();
        local.Kind.Should().NotBe(DateTimeKind.Utc);

        Dates.TryParse("2026-08-27T00:00:00", out var utc,
                       styles: DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
             .Should().BeTrue();
        utc.Kind.Should().Be(DateTimeKind.Utc);
    }

    // This is what fallbackStyles exists for: the known format must stay untouched while
    // the general fallback normalizes to UTC. Collapsing the two would run 27.08. through
    // AdjustToUniversal and, at a positive UTC offset, land on the 26th.
    [Fact]
    public void FallbackStyles_keep_the_exact_pass_from_being_shifted()
    {
        Dates.TryParse("27.08.2026", out var exact,
                       formats: new[] { "dd.MM.yyyy" },
                       culture: CultureInfo.InvariantCulture,
                       styles: DateTimeStyles.None,
                       fallbackStyles: DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
             .Should().BeTrue();
        exact.Date.Should().Be(new DateTime(2026, 8, 27), "the exact pass must not be normalized");

        // A value the exact formats do not cover goes through the fallback and is UTC.
        Dates.TryParse("2026-08-27T12:00:00+02:00", out var viaFallback,
                       formats: new[] { "dd.MM.yyyy" },
                       culture: CultureInfo.InvariantCulture,
                       styles: DateTimeStyles.None,
                       fallbackStyles: DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
             .Should().BeTrue();
        viaFallback.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("20240531193352.0Z", 2024, 5, 31)]
    [InlineData("20260101000000.0Z", 2026, 1, 1)]
    public void GeneralizedTime_parses_the_active_directory_form(string input, int y, int m, int d)
    {
        Dates.TryParseGeneralizedTime(input, out var parsed).Should().BeTrue();
        parsed.ToUniversalTime().Date.Should().Be(new DateTime(y, m, d));
    }

    [Fact]
    public void GeneralizedTime_falls_back_to_the_general_parser()
        => Dates.TryParseGeneralizedTime("2026-08-27", out var d).Should().BeTrue()
            .And.Subject.As<object>().Should().NotBeNull();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("garbage")]
    public void GeneralizedTime_rejects_junk(string? input)
        => Dates.TryParseGeneralizedTime(input, out _).Should().BeFalse();
}
