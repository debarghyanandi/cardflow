using Cardflow.Api.Boards;
using Cardflow.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Cardflow.Api.Tests;

public sealed class BoardCommandTests(BoardDatabase fixture) : IClassFixture<BoardDatabase>
{
    [Fact]
    public async Task SequenceNumbersIncreaseIndependentlyPerBoard()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var commands = new BoardCommandService(database, new BoardService(database), new BoardEventStore(database));
        var firstSession = new string('a', 64);
        var secondSession = new string('b', 64);
        var first = await commands.CreateBoardAsync(new("First", "Ada"), firstSession, default);
        var second = await commands.CreateBoardAsync(new("Second", "Lin"), secondSession, default);
        var firstColumn = await commands.CreateColumnAsync(first.Value.Token, firstSession, new("Now", null, null), default);
        var secondColumn = await commands.CreateColumnAsync(second.Value.Token, secondSession, new("Now", null, null), default);

        Assert.Equal([1L, 2L], [first.Event.Seq, firstColumn.Event.Seq]);
        Assert.Equal([1L, 2L], [second.Event.Seq, secondColumn.Event.Seq]);
        var firstSnapshot = await new BoardService(database).SnapshotAsync(first.Value.Token, firstSession, default);
        Assert.Equal(2, firstSnapshot.Seq);
    }

    [Fact]
    public async Task EventInsertFailureRollsBackTheCardAndSequence()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var commands = new BoardCommandService(database, new BoardService(database), new BoardEventStore(database));
        var session = new string('c', 64);
        var board = await commands.CreateBoardAsync(new("Atomic", "Ada"), session, default);
        var column = await commands.CreateColumnAsync(board.Value.Token, session, new("Now", null, null), default);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_card_event() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.type = 'CardCreated' THEN
                    RAISE EXCEPTION 'forced event failure';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER reject_card_event BEFORE INSERT ON board_events
            FOR EACH ROW EXECUTE FUNCTION reject_card_event();
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() => commands.CreateCardAsync(
            board.Value.Token, session, column.Value.Id, new("Should roll back", "", null, null), default));

        await using var verification = new CardflowDbContext(fixture.Options);
        Assert.False(await verification.Cards.AnyAsync(card => card.ColumnId == column.Value.Id));
        Assert.Equal(2, await verification.Boards.Where(candidate => candidate.Id == board.Value.Id)
            .Select(candidate => candidate.EventSeq).SingleAsync());
        Assert.Equal(2, await verification.BoardEvents.CountAsync(candidate => candidate.BoardId == board.Value.Id));
    }
}
