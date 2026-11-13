using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Refugio360.Api;
using Xunit;

namespace Refugio360.Api.Tests;

public sealed class SqliteSchemaUpdaterTests
{
    [Fact]
    public async Task Existing_database_gets_availability_column_and_legacy_alert_status()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        foreach (var sql in new[]
        {
            "CREATE TABLE \"SafeLocations\" (\"Id\" TEXT PRIMARY KEY);",
            "CREATE TABLE \"Alerts\" (\"Id\" TEXT PRIMARY KEY, \"Status\" TEXT NOT NULL);",
            "INSERT INTO \"Alerts\" (\"Id\", \"Status\") VALUES ('legacy-alert', 'Revisado');"
        })
        {
            using var setup = connection.CreateCommand();
            setup.CommandText = sql;
            await setup.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<RefugioDbContext>().UseSqlite(connection).Options;
        using var db = new RefugioDbContext(options);
        await SqliteSchemaUpdater.UpgradeAsync(db);
        await SqliteSchemaUpdater.UpgradeAsync(db);

        using var columnCheck = connection.CreateCommand();
        columnCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('SafeLocations') WHERE name = 'DisponibilidadeVerificadaEmUtc';";
        Assert.Equal(1L, Convert.ToInt64(await columnCheck.ExecuteScalarAsync()));

        using var statusCheck = connection.CreateCommand();
        statusCheck.CommandText = "SELECT \"Status\" FROM \"Alerts\" WHERE \"Id\" = 'legacy-alert';";
        Assert.Equal(AlertStatuses.Publicado, Convert.ToString(await statusCheck.ExecuteScalarAsync()));
    }
}
