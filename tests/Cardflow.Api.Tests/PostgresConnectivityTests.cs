using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Cardflow.Api.Tests;

public sealed class PostgresConnectivityTests
{
    [Fact]
    public async Task ConnectsToRealPostgres()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("cardflow_test")
            .WithUsername("cardflow_test")
            .WithPassword("cardflow_test")
            .Build();

        await postgres.StartAsync();

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_database()", connection);

        Assert.Equal("cardflow_test", await command.ExecuteScalarAsync());
    }
}
