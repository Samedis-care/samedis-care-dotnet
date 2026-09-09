using System.Data.Common;
using FluentAssertions;
using SamedisCare.Helper.Data;
using Xunit;

namespace SamedisCare.Helper.Tests;

// The connection-string builder is the part worth testing without a database: the copies
// this replaces used string interpolation, which breaks on values containing a semicolon
// or a quote. Query/Scalar are thin wrappers over DbProviderFactory and need a real
// provider, so they are covered by the tools' functional runs instead.
public class DatabaseTests
{
    private static string Value(string connectionString, string key)
    {
        var b = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return b.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
    }

    [Fact]
    public void SqlServer_without_a_port_uses_the_bare_server()
    {
        var cs = Database.BuildConnectionString(new DbConnectionSettings
        {
            DatabaseType = DbKind.SqlServer,
            Server = "sql01", Database = "staff", Username = "svc", Password = "pw",
        });

        Value(cs, "Data Source").Should().Be("sql01");
        Value(cs, "Initial Catalog").Should().Be("staff");
    }

    [Fact]
    public void SqlServer_with_a_port_appends_it_comma_separated()
    {
        var cs = Database.BuildConnectionString(new DbConnectionSettings
        {
            DatabaseType = DbKind.SqlServer,
            Server = "sql01", Port = "1433", Database = "staff",
        });

        Value(cs, "Data Source").Should().Be("sql01,1433");
    }

    [Fact]
    public void MySql_carries_the_public_key_flag()
    {
        var cs = Database.BuildConnectionString(new DbConnectionSettings
        {
            DatabaseType = DbKind.MySql,
            Server = "db", Port = "3306", Database = "d", Username = "u", Password = "p",
            AllowPublicKeyRetrieval = true,
        });

        Value(cs, "Server").Should().Be("db");
        Value(cs, "Port").Should().Be("3306");
        Value(cs, "AllowPublicKeyRetrieval").Should().Be("True");
    }

    [Fact]
    public void Sqlite_only_needs_the_file_path()
    {
        var cs = Database.BuildConnectionString(new DbConnectionSettings
        {
            DatabaseType = DbKind.Sqlite,
            Server = "/var/data/import.db", Username = "ignored", Password = "ignored",
        });

        Value(cs, "Data Source").Should().Be("/var/data/import.db");
        cs.Should().NotContain("ignored", "SQLite takes no credentials");
    }

    // This is what the interpolated versions got wrong. A password containing the
    // separator used to terminate the value and turn the rest into stray keywords.
    [Theory]
    [InlineData("pa;ss")]
    [InlineData("pa\"ss")]
    [InlineData("pa'ss")]
    [InlineData("pa;ss=word")]
    [InlineData("  spaced  ")]
    public void A_password_with_awkward_characters_survives_a_round_trip(string password)
    {
        var cs = Database.BuildConnectionString(new DbConnectionSettings
        {
            DatabaseType = DbKind.SqlServer,
            Server = "sql01", Database = "d", Username = "u", Password = password,
        });

        Value(cs, "Password").Should().Be(password);
        Value(cs, "Data Source").Should().Be("sql01", "the password must not bleed into other keys");
    }

    [Fact]
    public void A_server_name_with_a_separator_also_survives()
    {
        var cs = Database.BuildConnectionString(new DbConnectionSettings
        {
            DatabaseType = DbKind.Sqlite,
            Server = "/tmp/od;d.db",
        });

        Value(cs, "Data Source").Should().Be("/tmp/od;d.db");
    }

    [Fact]
    public void Missing_credentials_become_empty_rather_than_the_literal_null()
    {
        var cs = Database.BuildConnectionString(new DbConnectionSettings
        {
            DatabaseType = DbKind.SqlServer, Server = "s" });

        Value(cs, "User Id").Should().BeEmpty();
        cs.Should().NotContain("null");
    }

    [Fact]
    public void An_unknown_kind_is_rejected()
    {
        var act = () => Database.BuildConnectionString(new DbConnectionSettings { DatabaseType = (DbKind)99 });

        act.Should().Throw<NotSupportedException>();
    }

    // Guards the trap from the previous copies: a factory that yields no connection used
    // to surface as a NullReferenceException somewhere downstream.
    [Fact]
    public void A_factory_returning_no_connection_fails_with_a_clear_message()
    {
        var act = () => Database.Query(new NullFactory(), "Data Source=x", "select 1");

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*no connection*");
    }

    private sealed class NullFactory : DbProviderFactory
    {
        public override DbConnection? CreateConnection() => null;
    }
}

// DbTarget exists so a tool binds factory and settings once instead of threading both
// through every call. The connection string is built up front, so a bad kind fails at
// construction rather than at the first query.
public class DbTargetTests
{
    private sealed class NullFactory : DbProviderFactory
    {
        public override DbConnection? CreateConnection() => null;
    }

    [Fact]
    public void An_unsupported_kind_fails_at_construction()
    {
        var act = () => new DbTarget(new NullFactory(),
                                     new DbConnectionSettings { DatabaseType = (DbKind)99 });

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void A_valid_target_is_constructed_without_touching_the_database()
    {
        var act = () => new DbTarget(new NullFactory(), new DbConnectionSettings
        {
            DatabaseType = DbKind.Sqlite, Server = "/tmp/x.db",
        });

        act.Should().NotThrow("building the connection string must not open a connection");
    }

    [Fact]
    public void Queries_surface_the_factory_failure()
    {
        var target = new DbTarget(new NullFactory(), new DbConnectionSettings
        {
            DatabaseType = DbKind.Sqlite, Server = "/tmp/x.db",
        });

        ((Action)(() => target.Query("select 1"))).Should().Throw<InvalidOperationException>();
        ((Action)(() => target.Scalar("select 1"))).Should().Throw<InvalidOperationException>();
        ((Action)(() => target.QueryAsDataSet("select 1"))).Should().Throw<InvalidOperationException>();
    }
}
