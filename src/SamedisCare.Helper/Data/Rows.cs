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
    public static string Value(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column)) return string.Empty;
        var v = row[column];
        return v == null || v == DBNull.Value ? string.Empty : v.ToString() ?? string.Empty;
    }
}
