using Cardflow.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cardflow.Api.Boards;

public sealed class BoardEventPruner(IServiceScopeFactory scopes, ILogger<BoardEventPruner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await PruneAsync(scope.ServiceProvider.GetRequiredService<CardflowDbContext>(), stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Board event pruning failed; retrying next minute");
            }
        }
    }

    public static Task<int> PruneAsync(CardflowDbContext database, CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlRawAsync("""
            DELETE FROM board_events AS event
            USING boards AS board
            WHERE event.board_id = board.id
              AND (event.created_at < now() - interval '7 days'
                   OR event.seq <= board.event_seq - 5000)
            """, cancellationToken);
}
