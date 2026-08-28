using System.Data;
using System.Data.Common;

namespace SamedisCare.Helper.Data;

/// <summary>
/// Connection details for an import database, independent of the provider.
/// </summary>
public class DbConnectionSettings
{
    /// <summary>Server host, or the file path for SQLite.</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Optional port. Ignored by SQLite.</summary>
    public string? Port { get; set; }

    /// <summary>Database or catalog name. Ignored by SQLite.</summary>
    public string? Database { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>MySQL only: whether to allow public key retrieval.</summary>
    public bool AllowPublicKeyRetrieval { get; set; }
}

/// <summary>Provider families the tools connect to.</summary>
public enum DbKind
{
    SqlServer,
    MySql,
    Sqlite,
}

/// <summary>
/// Provider-agnostic database access, consolidating the <c>DbHelper</c> copies from
/// staff-sync and sync-trainings.
/// <para>
/// This class references no database driver. Queries take a <see cref="DbProviderFactory"/>
/// which the consuming tool supplies, so each tool keeps only the drivers it actually
/// needs and this package forces none on anyone. That also removes a trap the previous
/// copies had: sync-trainings built connection strings for four providers but its query
/// method only ever created a SQL Server factory, so a MySQL configuration passed
/// validation and failed later at execution.
/// </para>
/// </summary>
public static class Database
{
    /// <summary>
    /// Builds a connection string for <paramref name="kind"/>.
    /// <para>
    /// Uses <see cref="DbConnectionStringBuilder"/> rather than string interpolation, which
    /// is what the previous copies did. Interpolation breaks on a password containing a
    /// semicolon or a quote, and can append unintended keywords; the builder quotes and
    /// escapes each value.
    /// </para>
    /// </summary>
    public static string BuildConnectionString(DbKind kind, DbConnectionSettings settings)
    {
        var b = new DbConnectionStringBuilder();

        switch (kind)
        {
            case DbKind.SqlServer:
                b["Data Source"] = string.IsNullOrEmpty(settings.Port)
                    ? settings.Server
                    : $"{settings.Server},{settings.Port}";
                b["Initial Catalog"] = settings.Database ?? string.Empty;
                b["User Id"] = settings.Username ?? string.Empty;
                b["Password"] = settings.Password ?? string.Empty;
                break;

            case DbKind.MySql:
                b["Server"] = settings.Server;
                if (!string.IsNullOrEmpty(settings.Port)) b["Port"] = settings.Port;
                b["Database"] = settings.Database ?? string.Empty;
                b["User Id"] = settings.Username ?? string.Empty;
                b["Password"] = settings.Password ?? string.Empty;
                b["AllowPublicKeyRetrieval"] = settings.AllowPublicKeyRetrieval;
                break;

            case DbKind.Sqlite:
                // Server carries the file path here; there is no credential or catalog.
                b["Data Source"] = settings.Server;
                break;

            default:
                throw new NotSupportedException($"Unsupported database kind: {kind}");
        }

        return b.ConnectionString;
    }

    /// <summary>
    /// Runs a query and returns the rows as a <see cref="DataTable"/>.
    /// </summary>
    /// <param name="factory">
    /// The provider factory, e.g. <c>SqlClientFactory.Instance</c>. Supplied by the caller
    /// so this package needs no driver reference.
    /// </param>
    public static DataTable Query(DbProviderFactory factory, string connectionString, string sql)
    {
        using var connection = OpenConnection(factory, connectionString);
        using var command = CreateCommand(factory, connection, sql);
        using var reader = command.ExecuteReader();

        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    /// <summary>
    /// Runs a query and returns it wrapped in a <see cref="DataSet"/>, for callers that
    /// pass the result on to code expecting one.
    /// </summary>
    public static DataSet QueryAsDataSet(DbProviderFactory factory, string connectionString, string sql)
    {
        var set = new DataSet();
        set.Tables.Add(Query(factory, connectionString, sql));
        return set;
    }

    /// <summary>
    /// Runs a query expected to yield a single value. Returns null for no row, or for a
    /// row whose value is <see cref="DBNull"/>.
    /// </summary>
    public static string? Scalar(DbProviderFactory factory, string connectionString, string sql)
    {
        using var connection = OpenConnection(factory, connectionString);
        using var command = CreateCommand(factory, connection, sql);

        var result = command.ExecuteScalar();
        return result is null || result == DBNull.Value ? null : result.ToString();
    }

    private static DbConnection OpenConnection(DbProviderFactory factory, string connectionString)
    {
        var connection = factory.CreateConnection()
            ?? throw new InvalidOperationException("The provider factory returned no connection.");
        connection.ConnectionString = connectionString;
        connection.Open();
        return connection;
    }

    private static DbCommand CreateCommand(DbProviderFactory factory, DbConnection connection, string sql)
    {
        var command = factory.CreateCommand()
            ?? throw new InvalidOperationException("The provider factory returned no command.");
        command.Connection = connection;
        command.CommandText = sql;
        return command;
    }
}
