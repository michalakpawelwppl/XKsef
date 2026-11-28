using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

internal static class SqliteTestSchema
{
    internal static async Task CreateAsync(AppDbContext context, SqliteConnection connection)
    {
        if (!context.Database.IsSqlite() || !ReferenceEquals(context.Database.GetDbConnection(), connection)
            || new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource != ":memory:" || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("The SQLite fixture requires its own open in-memory connection.");

        connection.CreateCollation("Latin1_General_100_BIN2", (left, right) => string.Compare(left, right, StringComparison.Ordinal));
        connection.CreateFunction<byte[]?, int?>("DATALENGTH", value => value?.Length, isDeterministic: true);
        connection.CreateFunction<string?, int?>("LEN", value => value?.TrimEnd(' ').Length, isDeterministic: true);
        connection.CreateFunction<string?, int?>("ISJSON", IsJson, isDeterministic: true);

        // Preserve every table, index, FK and CHECK. Adapt only SQL Server-specific SQLite syntax.
        var schema = context.Database.GenerateCreateScript().Replace("varbinary(max)", "BLOB", StringComparison.Ordinal);
        schema = Regex.Replace(schema, @"(\bDEFAULT\s*\(?\s*)N(?=')", "$1", RegexOptions.CultureInvariant);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;\n" + schema;
        // A generated DDL script is literal SQL, not an EF composite-format string. JSON braces must survive intact.
        await command.ExecuteNonQueryAsync();
    }

    private static int? IsJson(string? value)
    {
        if (value is null) return null;
        try
        {
            using var json = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = int.MaxValue });
            // SQL Server's one-argument ISJSON accepts objects/arrays, not scalar JSON values.
            return json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? 1 : 0;
        }
        catch (JsonException) { return 0; }
    }
}
