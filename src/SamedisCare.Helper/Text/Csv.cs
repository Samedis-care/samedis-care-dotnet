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
    /// Reads a CSV file into a <see cref="DataTable"/>, detecting the encoding.
    /// </summary>
    /// <param name="filePath">The file to read.</param>
    /// <param name="hasHeader">Whether the first row names the columns.</param>
    /// <param name="delimiter">Field separator; <see cref="DefaultDelimiter"/> when omitted.</param>
    /// <param name="tableName">Optional name for the returned table.</param>
    /// <param name="trimFields">
    /// Trim surrounding whitespace from every field. Source exports routinely pad columns,
    /// and an untrimmed value fails an exact-match lookup for no visible reason.
    /// </param>
    /// <param name="encoding">
    /// Overrides detection. Leave null unless the file's encoding is known and its bytes
    /// would mislead <see cref="TextEncodings.Detect"/>.
    /// </param>
    /// <remarks>
    /// Malformed rows are ignored rather than throwing (<c>BadDataFound = null</c>), which is
    /// the behaviour the tools relied on for hand-maintained source files.
    /// <para>
    /// The encoding is detected rather than assumed. An earlier version of this method read
    /// every file as UTF-8, which silently replaced each umlaut in a Windows-1252 export —
    /// the usual output of German Excel — with a replacement character.
    /// </para>
    /// </remarks>
    public static DataTable Read(string filePath, bool hasHeader = true, string? delimiter = null,
                                 string? tableName = null, bool trimFields = false,
                                 Encoding? encoding = null)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = hasHeader,
            Delimiter = delimiter ?? DefaultDelimiter,
            DetectColumnCountChanges = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = trimFields ? TrimOptions.Trim : TrimOptions.None,
        };

        var table = tableName is null ? new DataTable() : new DataTable(tableName);
        using var reader = new StreamReader(filePath, encoding ?? TextEncodings.Detect(filePath),
                                            detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);
        using var dataReader = new CsvDataReader(csv);
        table.Load(dataReader);
        return table;
    }

    /// <summary>
    /// Appends a table's rows to a file, writing the header row only when creating it.
    /// </summary>
    /// <remarks>
    /// For the tools' running export and debug files, which grow across runs. Use
    /// <see cref="Write"/> where the file represents one run's complete output.
    /// </remarks>
    public static void Append(string filePath, DataTable table, string? delimiter = null)
    {
        var isNew = !File.Exists(filePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(filePath, append: true, TextEncodings.Utf8);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter ?? DefaultDelimiter,
            Quote = '"',
        });

        if (isNew)
        {
            foreach (DataColumn column in table.Columns) csv.WriteField(column.ColumnName);
            csv.NextRecord();
        }

        foreach (DataRow row in table.Rows)
        {
            foreach (DataColumn column in table.Columns) csv.WriteField(row[column]?.ToString());
            csv.NextRecord();
        }
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
