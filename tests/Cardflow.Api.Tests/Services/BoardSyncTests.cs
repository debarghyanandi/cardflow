using Cardflow.Api.BackgroundServices;
using Cardflow.Api.Data;
using Cardflow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cardflow.Api.Tests.Services;

public sealed class BoardSyncTests(BoardDatabase fixture) : IClassFixture<BoardDatabase>
{
    [Fact]
    public async Task ReconnectAfterFiftyChangesReplaysEverySequence()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var boardService = new BoardService(database);
        var commands = new BoardCommandService(database, boardService, new BoardEventStore(database));
        var session = new string('d', 64);
        var board = await commands.CreateBoardAsync(new("Replay", "Ada"), session, default);
        var seenSeq = board.Event.Seq;

        for (var index = 0; index < 50; index++)
            await commands.CreateColumnAsync(board.Value.Token, session, new($"Column {index}", null, null), default);

        var sync = await new BoardSyncService(database, boardService)
            .CatchUpAsync(board.Value.Token, session, seenSeq, default);

        Assert.Null(sync.Snapshot);
        Assert.Equal(51, sync.Seq);
        Assert.Equal(Enumerable.Range(2, 50).Select(value => (long)value), sync.Events.Select(boardEvent => boardEvent.Seq));
    }

    [Fact]
    public async Task MoreThanFiveHundredChangesReturnsSnapshot()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var boardService = new BoardService(database);
        var commands = new BoardCommandService(database, boardService, new BoardEventStore(database));
        var session = new string('e', 64);
        var board = await commands.CreateBoardAsync(new("Fallback", "Ada"), session, default);

        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO board_events (board_id, seq, type, payload, created_at)
            SELECT {board.Value.Id}, number, 'TestChange', jsonb_build_object(), now()
            FROM generate_series(2, 602) AS number;
            UPDATE boards SET event_seq = 602 WHERE id = {board.Value.Id};
            """);

        await using var reconnectDatabase = new CardflowDbContext(fixture.Options);
        var sync = await new BoardSyncService(reconnectDatabase, new BoardService(reconnectDatabase))
            .CatchUpAsync(board.Value.Token, session, board.Event.Seq, default);

        Assert.NotNull(sync.Snapshot);
        Assert.Equal(602, sync.Snapshot.Seq);
        Assert.Empty(sync.Events);
    }

    [Fact]
    public async Task PrunedEventGapAlsoReturnsSnapshot()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var commands = new BoardCommandService(database, service, new BoardEventStore(database));
        var session = new string('f', 64);
        var board = await commands.CreateBoardAsync(new("Pruning", "Ada"), session, default);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO board_events (board_id, seq, type, payload, created_at)
            SELECT {board.Value.Id}, number, 'TestChange', jsonb_build_object(),
                   CASE WHEN number = 6000 THEN now() - interval '8 days' ELSE now() END
            FROM generate_series(2, 6001) AS number;
            UPDATE boards SET event_seq = 6001 WHERE id = {board.Value.Id};
            """);

        var removed = await BoardEventPruner.PruneAsync(database, default);
        Assert.Equal(1002, removed);
        var retained = await database.BoardEvents.AsNoTracking()
            .Where(boardEvent => boardEvent.BoardId == board.Value.Id)
            .OrderBy(boardEvent => boardEvent.Seq).ToListAsync();
        Assert.Equal(4999, retained.Count);
        Assert.Equal(1002, retained[0].Seq);
        Assert.DoesNotContain(retained, boardEvent => boardEvent.Seq == 6000);

        await using var reconnectDatabase = new CardflowDbContext(fixture.Options);
        var sync = await new BoardSyncService(reconnectDatabase, new BoardService(reconnectDatabase))
            .CatchUpAsync(board.Value.Token, session, 5999, default);
        Assert.NotNull(sync.Snapshot);
        Assert.Empty(sync.Events);
    }
}
