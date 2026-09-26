using System.Data;
using Cardflow.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cardflow.Api.Ordering;

public sealed class RankMaintenance(IServiceScopeFactory scopes, ILogger<RankMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Rank maintenance failed; retrying on the next sweep");
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CardflowDbContext>();
        var crowdedColumns = await database.Cards.AsNoTracking()
            .Where(card => !card.IsArchived && card.Rank.Length > 50)
            .Select(card => card.ColumnId).Distinct().ToListAsync(cancellationToken);

        foreach (var columnId in crowdedColumns)
        {
            try
            {
                await RebalanceColumnAsync(database, columnId, cancellationToken);
            }
            catch (Exception exception) when (IsWriteConflict(exception))
            {
                database.ChangeTracker.Clear();
                logger.LogInformation("Rank rebalance for column {ColumnId} conflicted with a write; retrying next sweep", columnId);
            }
        }

        var crowdedBoards = await database.Columns.AsNoTracking()
            .Where(column => !column.IsArchived && column.Rank.Length > 50)
            .Select(column => column.BoardId).Distinct().ToListAsync(cancellationToken);
        foreach (var boardId in crowdedBoards)
        {
            try
            {
                await RebalanceBoardColumnsAsync(database, boardId, cancellationToken);
            }
            catch (Exception exception) when (IsWriteConflict(exception))
            {
                database.ChangeTracker.Clear();
                logger.LogInformation("Rank rebalance for board {BoardId} conflicted with a write; retrying next sweep", boardId);
            }
        }
    }

    private static bool IsWriteConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException or PostgresException { SqlState: "40001" } ||
        exception is DbUpdateException { InnerException: PostgresException { SqlState: "40001" } };

    public static async Task RebalanceColumnAsync(CardflowDbContext database, Guid columnId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var cards = await database.Cards
            .Where(card => card.ColumnId == columnId && !card.IsArchived)
            .OrderBy(card => card.Rank).ThenBy(card => card.Id)
            .ToListAsync(cancellationToken);
        if (cards.All(card => card.Rank.Length <= 50))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var ranks = FractionalRank.Spread(cards.Count);
        for (var index = 0; index < cards.Count; index++)
        {
            cards[index].Rank = ranks[index];
            cards[index].Version++;
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task RebalanceBoardColumnsAsync(CardflowDbContext database, Guid boardId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var columns = await database.Columns
            .Where(column => column.BoardId == boardId && !column.IsArchived)
            .OrderBy(column => column.Rank).ThenBy(column => column.Id)
            .ToListAsync(cancellationToken);
        if (columns.All(column => column.Rank.Length <= 50))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var ranks = FractionalRank.Spread(columns.Count);
        for (var index = 0; index < columns.Count; index++)
        {
            columns[index].Rank = ranks[index];
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
