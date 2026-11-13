using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Refugio360.Api;

internal static class SqliteSchemaUpdater
{
    public static async Task UpgradeAsync(RefugioDbContext db, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            using var columnCheck = connection.CreateCommand();
            columnCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('SafeLocations') WHERE name = 'DisponibilidadeVerificadaEmUtc';";
            var columnExists = Convert.ToInt64(await columnCheck.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!columnExists)
            {
                using var addColumn = connection.CreateCommand();
                addColumn.CommandText = "ALTER TABLE \"SafeLocations\" ADD COLUMN \"DisponibilidadeVerificadaEmUtc\" TEXT NULL;";
                await addColumn.ExecuteNonQueryAsync(cancellationToken);
            }

            // Alertas das versões anteriores já eram públicos; preservamos esse comportamento.
            using var legacyAlerts = connection.CreateCommand();
            legacyAlerts.CommandText = "UPDATE \"Alerts\" SET \"Status\" = 'Publicado' WHERE \"Status\" = 'Revisado';";
            await legacyAlerts.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }
}
