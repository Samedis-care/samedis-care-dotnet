using System.Data;
using FluentAssertions;
using SamedisCare.Helper.Text;
using Xunit;

namespace SamedisCare.Helper.Tests;

public class CsvTests : IDisposable
{
    private readonly List<string> _files = new();

    private string TempFile(string content = "")
    {
        var path = Path.Combine(Path.GetTempPath(), $"csv_{Guid.NewGuid():N}.csv");
        if (content.Length > 0) File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _files) if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void Reads_a_semicolon_file_with_a_header()
    {
        var path = TempFile("Personalnummer;Vorname;Nachname\n4711;Erika;Mustermann\n");

        var table = Csv.Read(path);

        table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)
             .Should().Equal("Personalnummer", "Vorname", "Nachname");
        table.Rows.Should().HaveCount(1);
        table.Rows[0]["Vorname"].Should().Be("Erika");
    }

    [Fact]
    public void Reads_umlauts_as_utf8()
    {
        var path = TempFile("Abteilung\nIntensivstation Süd\n");

        Csv.Read(path).Rows[0]["Abteilung"].Should().Be("Intensivstation Süd");
    }

    [Fact]
    public void A_custom_delimiter_is_honoured()
    {
        var path = TempFile("a,b\n1,2\n");

        Csv.Read(path, delimiter: ",").Columns.Count.Should().Be(2);
    }

    [Fact]
    public void Quoted_fields_containing_the_separator_stay_one_value()
    {
        var path = TempFile("Titel;Notiz\n\"Station A;B\";ok\n");

        Csv.Read(path).Rows[0]["Titel"].Should().Be("Station A;B");
    }

    [Fact]
    public void Write_then_read_round_trips_awkward_values()
    {
        var path = TempFile();
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "has;separator", "has\"quote", "plain" },
        };

        Csv.Write(path, new[] { "a", "b", "c" }, rows);
        var table = Csv.Read(path);

        table.Rows[0]["a"].Should().Be("has;separator");
        table.Rows[0]["b"].Should().Be("has\"quote");
        table.Rows[0]["c"].Should().Be("plain");
    }

    [Fact]
    public void Write_creates_a_missing_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"csvdir_{Guid.NewGuid():N}", "sub");
        var path = Path.Combine(dir, "out.csv");
        try
        {
            Csv.Write(path, new[] { "h" }, new List<IReadOnlyList<string>> { new[] { "v" } });
            File.Exists(path).Should().BeTrue();
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); }
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("has;sep", "\"has;sep\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\nbreak", "\"has\nbreak\"")]
    public void Escape_quotes_only_what_needs_it(string? input, string expected)
        => Csv.Escape(input).Should().Be(expected);

    [Fact]
    public void Escape_respects_a_custom_separator()
    {
        Csv.Escape("a;b", ",").Should().Be("a;b", "semicolon is not the separator here");
        Csv.Escape("a,b", ",").Should().Be("\"a,b\"");
    }

    [Fact]
    public void HasColumns_and_AvailableColumns_report_what_the_table_carries()
    {
        var table = new DataTable();
        table.Columns.Add("Vorname");
        table.Columns.Add("Nachname");

        Csv.HasColumns(table, new[] { "Vorname", "Nachname" }).Should().BeTrue();
        Csv.HasColumns(table, new[] { "Vorname", "Email" }).Should().BeFalse();
        // Equal(params) would read a trailing "because" string as another expected item,
        // so the expectation is passed as an explicit array.
        Csv.AvailableColumns(table, new[] { "Email", "Nachname", "Vorname" })
           .Should().Equal(new[] { "Nachname", "Vorname" });
    }

    [Fact]
    public void HasColumns_is_true_for_an_empty_requirement_list()
        => Csv.HasColumns(new DataTable(), Array.Empty<string>()).Should().BeTrue();
}
