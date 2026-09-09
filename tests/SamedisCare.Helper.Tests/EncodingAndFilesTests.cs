using System.Data;
using System.Text;
using FluentAssertions;
using SamedisCare.Helper.Data;
using SamedisCare.Helper.IO;
using SamedisCare.Helper.Text;
using Xunit;

namespace SamedisCare.Helper.Tests;

// Temporary files, cleaned up per test.
public sealed class TempFile : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "sc-test-" + Guid.NewGuid().ToString("N") + ".csv");

    public TempFile Write(byte[] bytes) { File.WriteAllBytes(Path, bytes); return this; }

    public TempFile Write(string text, Encoding encoding)
    {
        File.WriteAllBytes(Path, encoding.GetBytes(text));
        return this;
    }

    public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
}

public class TextEncodingsTests
{
    // The case that matters: a German Excel export has no BOM and is Windows-1252. Read as
    // UTF-8 it does not fail — it silently yields replacement characters.
    [Fact]
    public void A_bom_less_windows_1252_file_is_recognised()
    {
        using var f = new TempFile().Write("Gerät;Größe;Müller", TextEncodings.Windows1252);

        TextEncodings.Detect(f.Path).Should().BeSameAs(TextEncodings.Windows1252);
    }

    [Fact]
    public void Windows_1252_umlauts_survive_a_detected_read()
    {
        using var f = new TempFile().Write("Gerät;Größe", TextEncodings.Windows1252);

        var text = File.ReadAllText(f.Path, TextEncodings.Detect(f.Path));

        text.Should().Be("Gerät;Größe").And.NotContain("�");
    }

    [Fact]
    public void Reading_the_same_file_as_utf8_is_what_corrupts_it()
    {
        using var f = new TempFile().Write("Gerät", TextEncodings.Windows1252);

        File.ReadAllText(f.Path, Encoding.UTF8).Should().Contain("�",
            "this is the failure the detection exists to prevent");
    }

    [Fact]
    public void A_bom_less_utf8_file_is_recognised()
    {
        using var f = new TempFile().Write("Gerät;Größe", TextEncodings.Utf8);

        TextEncodings.Detect(f.Path).Should().BeSameAs(TextEncodings.Utf8);
    }

    [Theory]
    [InlineData(0xEF, 0xBB, 0xBF)]
    public void A_utf8_bom_is_recognised(byte a, byte b, byte c)
    {
        using var f = new TempFile().Write(new[] { a, b, c, (byte)'x' });

        TextEncodings.Detect(f.Path).Should().BeSameAs(TextEncodings.Utf8);
    }

    [Fact]
    public void A_utf16_little_endian_bom_is_recognised()
    {
        using var f = new TempFile().Write(new byte[] { 0xFF, 0xFE, (byte)'x', 0x00 });

        TextEncodings.Detect(f.Path).Should().Be(Encoding.Unicode);
    }

    [Fact]
    public void A_utf16_big_endian_bom_is_recognised()
    {
        using var f = new TempFile().Write(new byte[] { 0xFE, 0xFF, 0x00, (byte)'x' });

        TextEncodings.Detect(f.Path).Should().Be(Encoding.BigEndianUnicode);
    }

    [Fact]
    public void A_utf32_little_endian_bom_wins_over_the_utf16_prefix()
    {
        using var f = new TempFile().Write(new byte[] { 0xFF, 0xFE, 0x00, 0x00 });

        TextEncodings.Detect(f.Path).Should().Be(Encoding.UTF32,
            "the UTF-32 mark starts with the UTF-16 one, so order of checks matters");
    }

    // An invalid byte can sit anywhere, so the probe has to read the whole file.
    [Fact]
    public void An_invalid_byte_late_in_a_long_file_is_still_found()
    {
        var bytes = Encoding.ASCII.GetBytes(new string('a', 20_000)).ToList();
        bytes.Add(0xE4); // lone Windows-1252 'ä'
        using var f = new TempFile().Write(bytes.ToArray());

        TextEncodings.Detect(f.Path).Should().BeSameAs(TextEncodings.Windows1252);
    }

    [Fact]
    public void Pure_ascii_reads_as_utf8()
    {
        using var f = new TempFile().Write("a;b;c", Encoding.ASCII);

        TextEncodings.Detect(f.Path).Should().BeSameAs(TextEncodings.Utf8);
    }
}

public class FilesTests
{
    [Fact]
    public void A_missing_file_counts_as_empty()
        => Files.IsEffectivelyEmpty(Path.Combine(Path.GetTempPath(), "sc-does-not-exist-" + Guid.NewGuid())).Should().BeTrue();

    [Fact]
    public void A_zero_byte_file_counts_as_empty()
    {
        using var f = new TempFile().Write(Array.Empty<byte>());
        Files.IsEffectivelyEmpty(f.Path).Should().BeTrue();
    }

    // The case worth having: an exporter with nothing to write still emits a BOM, and
    // treating that as a malformed import produces a daily ERROR for a normal situation.
    [Fact]
    public void A_bom_only_file_counts_as_empty()
    {
        using var f = new TempFile().Write(new byte[] { 0xEF, 0xBB, 0xBF });
        Files.IsEffectivelyEmpty(f.Path).Should().BeTrue();
    }

    [Fact]
    public void A_utf16_bom_only_file_counts_as_empty()
    {
        using var f = new TempFile().Write(new byte[] { 0xFF, 0xFE });
        Files.IsEffectivelyEmpty(f.Path).Should().BeTrue();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\r\n\r\n")]
    [InlineData("\t \n")]
    public void A_whitespace_only_file_counts_as_empty(string content)
    {
        using var f = new TempFile().Write(content, TextEncodings.Utf8);
        Files.IsEffectivelyEmpty(f.Path).Should().BeTrue();
    }

    [Fact]
    public void A_file_with_a_header_only_is_not_empty()
    {
        using var f = new TempFile().Write("a;b;c\r\n", TextEncodings.Utf8);
        Files.IsEffectivelyEmpty(f.Path).Should().BeFalse("a header is data as far as this check goes");
    }

    [Theory]
    [InlineData("report.pdf", null, null, ".pdf")]
    [InlineData("image.PNG", null, null, ".PNG")]
    [InlineData(null, null, "https://host/path/file.jpg", ".jpg")]
    [InlineData(null, null, "https://host/path/file.jpg?v=2", ".jpg")]
    [InlineData(null, "application/pdf", null, ".pdf")]
    [InlineData(null, "image/png", null, ".png")]
    [InlineData(null, "IMAGE/JPEG", null, ".jpg")]
    public void An_extension_is_taken_from_whatever_the_source_offers(
        string? name, string? mime, string? url, string expected)
        => Files.Extension(name, mime, url, ".bin").Should().Be(expected);

    [Fact]
    public void The_file_name_wins_over_the_url_and_the_mime_type()
        => Files.Extension("a.csv", "application/pdf", "https://h/b.png", ".bin").Should().Be(".csv");

    [Fact]
    public void The_url_wins_over_the_mime_type()
        => Files.Extension(null, "application/pdf", "https://h/b.png", ".bin").Should().Be(".png");

    // The version this replaces hardcoded .pdf, which is right for a document download and
    // wrong everywhere else — so the caller has to say.
    [Fact]
    public void The_fallback_is_the_callers_choice()
    {
        Files.Extension(null, null, null, ".pdf").Should().Be(".pdf");
        Files.Extension(null, null, null, ".png").Should().Be(".png");
        Files.Extension(null, "application/octet-stream", null, ".dat").Should().Be(".dat");
    }
}

public class NumberFormatTests
{
    [Theory]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("0,5", 0.5)]
    [InlineData("-3,25", -3.25)]
    public void The_german_convention_parses(string text, double expected)
    {
        NumberFormat.Comma.TryParseDecimal(text, out var result).Should().BeTrue();
        result.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    public void The_invariant_convention_parses(string text, double expected)
    {
        NumberFormat.Dot.TryParseDecimal(text, out var result).Should().BeTrue();
        result.Should().Be((decimal)expected);
    }

    [Fact]
    public void Formatting_uses_the_configured_separator()
    {
        NumberFormat.Comma.Format(1234.5m).Should().Be("1234,50");
        NumberFormat.Dot.Format(1234.5m).Should().Be("1234.50");
    }

    [Fact]
    public void Only_comma_and_dot_are_accepted()
        => ((Action)(() => new NumberFormat(';'))).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(null, ',')]
    [InlineData("", ',')]
    [InlineData(".", '.')]
    [InlineData(",", ',')]
    public void A_setting_maps_onto_a_format(string? setting, char expected)
        => NumberFormat.FromSetting(setting).DecimalSeparator.Should().Be(expected);

    // The version this replaces held the separator in a mutable static, so the meaning of a
    // parse depended on whatever unrelated code had assigned last.
    [Fact]
    public void Two_formats_do_not_interfere()
    {
        var de = new NumberFormat(',');
        var iv = new NumberFormat('.');

        de.TryParseDecimal("1,5", out var a).Should().BeTrue();
        iv.TryParseDecimal("1.5", out var b).Should().BeTrue();
        a.Should().Be(b).And.Be(1.5m);
    }
}

public class CsvReadDetectionTests
{
    [Fact]
    public void A_windows_1252_csv_keeps_its_umlauts()
    {
        using var f = new TempFile().Write("titel;hersteller\r\nGerät;Müller AG\r\n",
                                           TextEncodings.Windows1252);

        var table = Csv.Read(f.Path);

        table.Rows[0]["titel"].Should().Be("Gerät");
        table.Rows[0]["hersteller"].Should().Be("Müller AG");
    }

    [Fact]
    public void Fields_can_be_trimmed_on_read()
    {
        using var f = new TempFile().Write("a;b\r\n  x  ;  y  \r\n", TextEncodings.Utf8);

        Csv.Read(f.Path, trimFields: true).Rows[0]["a"].Should().Be("x");
        Csv.Read(f.Path, trimFields: false).Rows[0]["a"].Should().Be("  x  ");
    }

    [Fact]
    public void The_table_can_be_named()
    {
        using var f = new TempFile().Write("a\r\n1\r\n", TextEncodings.Utf8);

        Csv.Read(f.Path, tableName: "inventories").TableName.Should().Be("inventories");
    }

    [Fact]
    public void Append_writes_the_header_once()
    {
        using var f = new TempFile();
        File.Delete(f.Path);

        var table = new DataTable();
        table.Columns.Add("a");
        table.Rows.Add("1");

        Csv.Append(f.Path, table);
        Csv.Append(f.Path, table);

        var lines = File.ReadAllLines(f.Path).Where(l => l.Length > 0).ToList();
        lines.Should().Equal("a", "1", "1");
    }
}

public class RowsAndBoolTests
{
    private static DataRow RowWith(string column, object? value)
    {
        var table = new DataTable();
        table.Columns.Add(column);
        var row = table.NewRow();
        row[column] = value ?? DBNull.Value;
        table.Rows.Add(row);
        return row;
    }

    [Fact]
    public void An_absent_column_yields_empty()
        => Rows.Value(RowWith("a", "x"), "b").Should().BeEmpty();

    [Fact]
    public void DbNull_yields_empty()
        => Rows.Value(RowWith("a", null), "a").Should().BeEmpty();

    // Source exports pad columns, and an untrimmed value fails an exact-match lookup for no
    // visible reason.
    [Theory]
    [InlineData("  x  ", "x")]
    [InlineData("\tx\n", "x")]
    [InlineData("x", "x")]
    public void Values_are_trimmed(string stored, string expected)
        => Rows.Value(RowWith("a", stored), "a").Should().Be(expected);

    // A SQL export to CSV writes an absent value as the four characters NULL. Taken
    // literally, that string reaches the API as if it were the device's name.
    [Theory]
    [InlineData("NULL")]
    [InlineData("null")]
    [InlineData("Null")]
    [InlineData("  NULL  ")]
    public void The_literal_text_NULL_is_treated_as_absent(string stored)
        => Rows.Value(RowWith("a", stored), "a").Should().BeEmpty();

    [Fact]
    public void A_value_that_merely_contains_NULL_is_kept()
        => Rows.Value(RowWith("a", "NULLIFY"), "a").Should().Be("NULLIFY");

    // y/n come from external-sync's source exports.
    [Theory]
    [InlineData("y", true)]
    [InlineData("Y", true)]
    [InlineData("n", false)]
    [InlineData("N", false)]
    [InlineData("ja", true)]
    [InlineData("nein", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    [InlineData(" yes ", true)]
    public void Boolean_spellings_the_sources_use_all_parse(string text, bool expected)
    {
        Strings.TryParseBool(text, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2")]
    public void Anything_else_does_not_parse(string? text)
        => Strings.TryParseBool(text, out _).Should().BeFalse();

    [Theory]
    [InlineData("4711", 4711L)]
    [InlineData("-3", -3L)]
    public void Longs_parse_invariantly(string text, long expected)
    {
        Strings.TryParseLong(text, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    // Invariant on purpose: a group separator has no business in an identifier.
    [Fact]
    public void A_long_with_a_group_separator_does_not_parse()
        => Strings.TryParseLong("1.234", out _).Should().BeFalse();
}
