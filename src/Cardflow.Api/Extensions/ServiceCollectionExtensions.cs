using Cardflow.Api.BackgroundServices;
using Cardflow.Api.Data;
using Cardflow.Api.Providers;
using Cardflow.Api.Realtime;
using Cardflow.Api.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Cardflow.Api.Extensions;

/// <summary>
/// Registers everything Cardflow's API needs: the database, the services that
/// hold board logic, Redis (the SignalR backplane and presence), the SignalR
/// hub, and the background jobs that keep ranks, presence and the event log
/// healthy. Mirrors your reference project's <c>RegisterApiDependencies</c> —
/// one method, called once from <c>Program.cs</c>, so the composition root
/// stays thin.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCardflowServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers();

        services.AddDbContext<CardflowDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddScoped<BoardService>();
        services.AddScoped<BoardEventStore>();
        services.AddScoped<BoardCommandService>();
        services.AddScoped<BoardSyncService>();
        services.AddSingleton<BoardEventPublisher>();

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
            configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("A Redis connection string is required.")));
        services.AddSingleton<BoardPresenceProvider>();

        services.AddHostedService<PresenceSweep>();
        services.AddHostedService<RankMaintenance>();
        services.AddHostedService<BoardEventPruner>();

        services.AddSignalR().AddStackExchangeRedis(
            configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("A Redis connection string is required."));

        return services;
    }
}
