using Cardflow.Api.BackgroundServices;
using Cardflow.Api.Data;
using Cardflow.Api.Data.Entities;
using Cardflow.Api.Exceptions;
using Cardflow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cardflow.Api.Tests.Services;

public sealed class BoardServiceTests(BoardDatabase fixture) : IClassFixture<BoardDatabase>
{
    [Fact]
    public async Task InsertingBetweenCardsWritesOnlyTheNewCardRow()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var session = new string('a', 64);
        var board = await service.CreateBoardAsync(new("Test board", "Ada"), session, default);
        var column = await service.CreateColumnAsync(board.Token, session, new("Doing", null, null), default);
        var first = await service.CreateCardAsync(board.Token, session, column.Id, new("First", "", null, null), default);
        var last = await service.CreateCardAsync(board.Token, session, column.Id, new("Last", "", null, null), default);

        await database.Database.ExecuteSqlRawAsync("""
            CREATE TABLE card_write_audit (operation text NOT NULL);
            CREATE FUNCTION audit_card_write() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                INSERT INTO card_write_audit(operation) VALUES (TG_OP);
                RETURN NEW;
            END $$;
            CREATE TRIGGER audit_card_write AFTER INSERT OR UPDATE OR DELETE ON cards
            FOR EACH ROW EXECUTE FUNCTION audit_card_write();
            """);

        var middle = await service.CreateCardAsync(board.Token, session, column.Id,
            new("Middle", "", first.Id, last.Id), default);
        var writes = await database.Database.SqlQueryRaw<string>(
            "SELECT operation AS \"Value\" FROM card_write_audit").ToListAsync();
        var snapshot = await service.SnapshotAsync(board.Token, session, default);

        Assert.Equal(["INSERT"], writes);
        Assert.Equal([first.Id, middle.Id, last.Id], snapshot.Columns.Single().Cards.Select(card => card.Id));
    }

    [Fact]
    public async Task EqualRanksAlwaysBreakTiesByCardId()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var session = new string('b', 64);
        var board = await service.CreateBoardAsync(new("Ties", "Lin"), session, default);
        var column = await service.CreateColumnAsync(board.Token, session, new("Now", null, null), default);
        var largerId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var smallerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        database.Cards.AddRange(
            new Card { Id = largerId, ColumnId = column.Id, Title = "Later", Rank = "1", Version = 1 },
            new Card { Id = smallerId, ColumnId = column.Id, Title = "Earlier", Rank = "1", Version = 1 });
        await database.SaveChangesAsync();

        var snapshot = await service.SnapshotAsync(board.Token, session, default);
        Assert.Equal([smallerId, largerId], snapshot.Columns.Single().Cards.Select(card => card.Id));
    }

    [Fact]
    public async Task StaleCardEditIsRejectedWithoutOverwritingTheWinner()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var session = new string('c', 64);
        var board = await service.CreateBoardAsync(new("Versions", "Lee"), session, default);
        var column = await service.CreateColumnAsync(board.Token, session, new("Now", null, null), default);
        var card = await service.CreateCardAsync(board.Token, session, column.Id, new("Original", "", null, null), default);
        var winner = await service.EditCardAsync(board.Token, session, card.Id, new("Winner", "", card.Version), default);

        var problem = await Assert.ThrowsAsync<BoardProblem>(() =>
            service.EditCardAsync(board.Token, session, card.Id, new("Loser", "", card.Version), default));
        var snapshot = await service.SnapshotAsync(board.Token, session, default);

        Assert.Equal(409, problem.StatusCode);
        Assert.Equal("Winner", snapshot.Columns.Single().Cards.Single().Title);
        Assert.Equal(2, winner.Version);
    }

    [Fact]
    public async Task MembershipIsCheckedForEveryBoardOperation()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var board = await service.CreateBoardAsync(new("Private link", "Owner"), new string('d', 64), default);

        var problem = await Assert.ThrowsAsync<BoardProblem>(() =>
            service.CreateColumnAsync(board.Token, new string('e', 64), new("No access", null, null), default));

        Assert.Equal(403, problem.StatusCode);
    }

    [Fact]
    public async Task AGuestCanJoinByTokenAndReadTheBoard()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var board = await service.CreateBoardAsync(new("Shared", "Owner"), new string('2', 64), default);
        var guest = await service.JoinAsync(board.Token, new("Guest"), new string('3', 64), default);

        var snapshot = await service.SnapshotAsync(board.Token, new string('3', 64), default);

        Assert.Equal("Guest", guest.Nickname);
        Assert.Equal(2, snapshot.Members.Count);
        Assert.Contains(snapshot.Members, member => member.Id == guest.Id);
    }

    [Fact]
    public async Task MovingAndArchivingACardPreservesItsDatabaseRow()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var session = new string('4', 64);
        var board = await service.CreateBoardAsync(new("Move", "Owner"), session, default);
        var firstColumn = await service.CreateColumnAsync(board.Token, session, new("First", null, null), default);
        var secondColumn = await service.CreateColumnAsync(board.Token, session, new("Second", null, null), default);
        var card = await service.CreateCardAsync(board.Token, session, firstColumn.Id, new("Keep me", "", null, null), default);

        var moved = await service.MoveCardAsync(board.Token, session, card.Id,
            new(secondColumn.Id, null, null, card.Version), default);
        var afterMove = await service.SnapshotAsync(board.Token, session, default);
        await service.ArchiveCardAsync(board.Token, session, card.Id, new(moved.Version), default);
        var afterArchive = await service.SnapshotAsync(board.Token, session, default);

        Assert.Equal(secondColumn.Id, moved.ColumnId);
        Assert.Equal(card.Id, afterMove.Columns.Single(column => column.Id == secondColumn.Id).Cards.Single().Id);
        Assert.Empty(afterArchive.Columns.SelectMany(column => column.Cards));
        Assert.True(await database.Cards.AnyAsync(candidate => candidate.Id == card.Id && candidate.IsArchived));
    }

    [Fact]
    public async Task LongRanksAreRebalancedWithoutChangingCardOrder()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var session = new string('f', 64);
        var board = await service.CreateBoardAsync(new("Crowded", "Kim"), session, default);
        var column = await service.CreateColumnAsync(board.Token, session, new("Now", null, null), default);
        var first = new Card
        {
            Id = Guid.NewGuid(), ColumnId = column.Id, Title = "First",
            Rank = new string('0', 51) + "1", Version = 1
        };
        var second = new Card
        {
            Id = Guid.NewGuid(), ColumnId = column.Id, Title = "Second",
            Rank = "1", Version = 1
        };
        database.Cards.AddRange(first, second);
        await database.SaveChangesAsync();
        Assert.Equal(1, await database.Cards.CountAsync(card => card.Rank.Length > 50 && card.ColumnId == column.Id));

        var boardEvent = await RankMaintenance.RebalanceColumnAsync(database, new BoardEventStore(database), column.Id, default);
        var snapshot = await service.SnapshotAsync(board.Token, session, default);

        Assert.Equal([first.Id, second.Id], snapshot.Columns.Single().Cards.Select(card => card.Id));
        Assert.All(snapshot.Columns.Single().Cards, card => Assert.True(card.Rank.Length < 50));
        Assert.All(snapshot.Columns.Single().Cards, card => Assert.Equal(2, card.Version));
        Assert.Equal("CardsRebalanced", boardEvent?.Type);
    }

    [Fact]
    public async Task LongColumnRanksAreRebalancedWithoutChangingTheirOrder()
    {
        await using var database = new CardflowDbContext(fixture.Options);
        var service = new BoardService(database);
        var session = new string('1', 64);
        var board = await service.CreateBoardAsync(new("Columns", "Mo"), session, default);
        var first = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Title = "First",
            Rank = new string('0', 51) + "1"
        };
        var second = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Title = "Second", Rank = "1"
        };
        database.Columns.AddRange(first, second);
        await database.SaveChangesAsync();
        Assert.Equal(1, await database.Columns.CountAsync(candidate => candidate.Rank.Length > 50 && candidate.BoardId == board.Id));

        var boardEvent = await RankMaintenance.RebalanceBoardColumnsAsync(database, new BoardEventStore(database), board.Id, default);
        var snapshot = await service.SnapshotAsync(board.Token, session, default);

        Assert.Equal([first.Id, second.Id], snapshot.Columns.Select(column => column.Id));
        Assert.All(snapshot.Columns, column => Assert.True(column.Rank.Length < 50));
        Assert.Equal("ColumnsRebalanced", boardEvent?.Type);
    }
}
