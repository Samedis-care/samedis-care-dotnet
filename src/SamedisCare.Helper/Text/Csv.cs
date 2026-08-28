using System.Data;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace SamedisCare.Helper.Text;

/// <summary>
/// CSV reading and writing shared by the sync tools. Consolidates the
/// <c>ReadCsvWithCsvHelper</c>, <c>WriteCsv</c>, <c>EscapeCsv</c>,
/// <c>CheckColumnsExist</c> and <c>GetAvailableColumns</c> copies.
/// </summary>
public static class Csv
{
    /// <summary>
    /// Default field separator. Semicolon, because the tools' source exports are German
    /// Excel/CSV files. Overridable per call.
    /// </summary>
    public const string DefaultDelimiter = ";";

    /// <summary>
    /// Reads a UTF-8 CSV file into a <see cref="DataTable"/>.
    /// <para>
    /// Malformed rows are ignored rather than throwing (<c>BadDataFound = null</c>), which
    /// is the behaviour the tools relied on for hand-maintained source files.
    /// </para>
    /// </summary>
    public static DataTable Read(string filePath, bool hasHeader = true, string? delimiter = null)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = hasHeader,
            Delimiter = delimiter ?? DefaultDelimiter,
            Encoding = Encoding.UTF8,
            DetectColumnCountChanges = true,
            BadDataFound = null,
        };

        var table = new DataTable();
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        using var csv = new CsvReader(reader, config);
        using var dataReader = new CsvDataReader(csv);
        table.Load(dataReader);
        return table;
    }

    /// <summary>
    /// Writes a header row and data rows, overwriting the file. Creates the directory when
    /// the path has one.
    /// </summary>
    public static void Write(string filePath, IReadOnlyList<string> headers,
                             IEnumerable<IReadOnlyList<string>> rows, string? delimiter = null)
    {
        var sep = delimiter ?? DefaultDelimiter;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
        writer.WriteLine(string.Join(sep, headers.Select(h => Escape(h, sep))));
        foreach (var row in rows)
            writer.WriteLine(string.Join(sep, row.Select(v => Escape(v, sep))));
    }

    /// <summary>
    /// Quotes a field when it contains the separator, a quote or a line break, doubling
    /// embedded quotes as the format requires. Returns an empty string for null.
    /// </summary>
    public static string Escape(string? value, string? delimiter = null)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sep = delimiter ?? DefaultDelimiter;
        var needsQuotes = value.Contains('"') || value.Contains(sep)
                          || value.Contains('\r') || value.Contains('\n');
        var sanitized = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{sanitized}\"" : sanitized;
    }

    /// <summary>True when every one of <paramref name="requiredColumns"/> is present.</summary>
    public static bool HasColumns(DataTable table, IEnumerable<string> requiredColumns)
        => requiredColumns.All(c => table.Columns.Contains(c));

    /// <summary>
    /// Returns those of <paramref name="wantedColumns"/> that the table actually has, in
    /// the order given — used to build an import mapping from an optional column list.
    /// </summary>
    public static string[] AvailableColumns(DataTable table, IEnumerable<string> wantedColumns)
        => wantedColumns.Where(c => table.Columns.Contains(c)).ToArray();
}
