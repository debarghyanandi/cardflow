using Cardflow.Api.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Cardflow.Api.Tests.Services;

public sealed class BoardDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public DbContextOptions<CardflowDbContext> Options => new DbContextOptionsBuilder<CardflowDbContext>()
        .UseNpgsql(_postgres.GetConnectionString()).Options;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = new CardflowDbContext(Options);
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
