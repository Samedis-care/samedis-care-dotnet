using System.Data;

namespace SamedisCare.Helper.Data;

/// <summary>
/// Safe reads from a <see cref="DataRow"/>, for import sources whose column set varies.
/// </summary>
public static class Rows
{
    /// <summary>
    /// Returns the column's value as a string, or an empty string when the column is
    /// absent or the value is <see cref="DBNull"/>. A missing column is a normal case for
    /// these imports, so it is not an error.
    /// </summary>
    /// <summary>
    /// A row's value as a trimmed string: empty when the column is absent, null,
    /// <see cref="DBNull"/>, or the literal text <c>NULL</c>.
    /// </summary>
    /// <remarks>
    /// Both normalisations matter for the files these tools read. Source exports pad columns,
    /// and an untrimmed value fails an exact-match lookup for no visible reason. And a SQL
    /// export to CSV writes an absent value as the four characters <c>NULL</c> — taken
    /// literally, that string is then sent to the API as if it were the device's name.
    /// </remarks>
    public static string Value(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column)) return string.Empty;

        var v = row[column];
        if (v == null || v == DBNull.Value) return string.Empty;

        var text = v.ToString()?.Trim() ?? string.Empty;
        return string.Equals(text, "NULL", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
    }
}
