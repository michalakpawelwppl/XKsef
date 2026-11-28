using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class SqliteTestSchemaTests
{
    [Fact]
    public async Task CreatesTheEntireCurrentModelWithJsonDefaultsChecksAndForeignKeysIntact()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var source = context.Database.GenerateCreateScript(); Assert.Contains("N'{\"Version\":1}'", source); Assert.Contains("N'{}'", source);
        await SqliteTestSchema.CreateAsync(context, connection);
        var tables = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name,sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await command.ExecuteReaderAsync(); while (await reader.ReadAsync()) tables.Add(reader.GetString(0), reader.GetString(1));
        }
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        Assert.Equal(model.Tables.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal), tables.Keys.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var table in model.Tables)
            foreach (var check in table.CheckConstraints)
            {
                Assert.Contains(Assert.IsType<string>(check.Name), tables[table.Name]); Assert.Contains(check.Sql, tables[table.Name]);
            }
        await using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA foreign_keys"; Assert.Equal(1L, await pragma.ExecuteScalarAsync());
        Assert.Equal("{\"Version\":1}", await EvaluateDefaultAsync(connection, "SalesInvoices", "Fa3"));
        Assert.Equal("{}", await EvaluateDefaultAsync(connection, "SalesInvoiceLines", "Annex15"));
    }

    [Theory]
    [InlineData(null, null)] [InlineData("{}", 1)] [InlineData("[]", 1)] [InlineData("{\"n\":1}", 1)]
    [InlineData("not-json", 0)] [InlineData("1", 0)] [InlineData("\"text\"", 0)] [InlineData("null", 0)] [InlineData("true", 0)]
    public async Task JsonFunctionRetainsSqlServerRootSemanticsAndDoesNotBypassCheckConstraints(string? value, int? expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await SqliteTestSchema.CreateAsync(context, connection);
        await using var command = connection.CreateCommand(); command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        command.CommandText = "SELECT ISJSON($value)"; var actual = await command.ExecuteScalarAsync();
        if (expected is null) Assert.IsType<DBNull>(actual); else Assert.Equal((long)expected.Value, actual);
        command.Parameters.Clear(); command.CommandText = "CREATE TABLE JsonFixtureProbe (Value TEXT CHECK (ISJSON(Value)=1))"; await command.ExecuteNonQueryAsync();
        command.CommandText = "INSERT INTO JsonFixtureProbe (Value) VALUES ($value)"; command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        if (expected == 0) Assert.Equal(19, (await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync())).SqliteErrorCode);
        else Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task RefusesFileBackedDatabasesWithoutOpeningOrCreatingAFile()
    {
        var file = Path.Combine(Path.GetTempPath(), "xksef-fixture-guard-" + Guid.NewGuid().ToString("N") + ".db");
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ConnectionString);
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqliteTestSchema.CreateAsync(context, connection));
        Assert.Equal(ConnectionState.Closed, connection.State); Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task RefusesAConnectionThatDoesNotBelongToTheContext()
    {
        await using var owner = new SqliteConnection("Data Source=:memory:"); await owner.OpenAsync();
        await using var other = new SqliteConnection("Data Source=:memory:"); await other.OpenAsync();
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(owner).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqliteTestSchema.CreateAsync(context, other));
    }

    private static async Task<object?> EvaluateDefaultAsync(SqliteConnection connection, string table, string column)
    {
        string expression;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT dflt_value FROM pragma_table_info($table) WHERE name=$column";
            command.Parameters.AddWithValue("$table", table); command.Parameters.AddWithValue("$column", column);
            expression = Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        await using var evaluate = connection.CreateCommand(); evaluate.CommandText = "SELECT " + expression;
        return await evaluate.ExecuteScalarAsync();
    }
}
