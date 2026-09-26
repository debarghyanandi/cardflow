using Cardflow.Api.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Cardflow.Api.Tests.Data;

public sealed class SchemaTests
{
    [Fact]
    public async Task MigrationCreatesTheBoardTablesInPostgres()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CardflowDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var database = new CardflowDbContext(options);
        await database.Database.MigrateAsync();

        var count = await database.Database
            .SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('boards', 'board_members', 'columns', 'cards', 'board_events')")
            .SingleAsync();
        Assert.Equal(5, count);
        var textRanks = await database.Database.SqlQueryRaw<int>(
            "SELECT count(*)::integer AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name IN ('columns', 'cards') AND column_name = 'rank' AND data_type = 'text'")
            .SingleAsync();
        Assert.Equal(2, textRanks);
        var sequenceColumns = await database.Database.SqlQueryRaw<int>(
            "SELECT count(*)::integer AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND ((table_name = 'boards' AND column_name = 'event_seq') OR (table_name = 'board_events' AND column_name = 'seq'))")
            .SingleAsync();
        Assert.Equal(2, sequenceColumns);
    }
}
