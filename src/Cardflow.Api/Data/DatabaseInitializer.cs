using Microsoft.EntityFrameworkCore;

namespace Cardflow.Api.Data;

public static class DatabaseInitializer
{
    public static async Task MigrateAsync(CardflowDbContext database, CancellationToken cancellationToken = default)
    {
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await LockAsync(database, "SELECT pg_advisory_lock(1762636321)", cancellationToken);
            try
            {
                await database.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await LockAsync(database, "SELECT pg_advisory_unlock(1762636321)", CancellationToken.None);
            }
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }
    }

    private static async Task LockAsync(CardflowDbContext database, string sql, CancellationToken cancellationToken)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
