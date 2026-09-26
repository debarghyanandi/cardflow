using System.Security.Cryptography;
using System.Data;
using Cardflow.Api.Data;
using Cardflow.Api.Ordering;
using Microsoft.EntityFrameworkCore;

namespace Cardflow.Api.Boards;

public sealed class BoardService(CardflowDbContext database)
{
    private static readonly string[] Colours = ["#d65a4a", "#437fc7", "#43a38b", "#a56ac9", "#d49036"];

    public async Task<BoardCreated> CreateBoardAsync(CreateBoardRequest request, string session, CancellationToken cancellationToken)
    {
        var title = Required(request.Title, "Board title", 120);
        var nickname = Required(request.Nickname, "Nickname", 40);
        var board = new Board
        {
            Id = Guid.NewGuid(),
            Title = title,
            JoinToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow
        };
        database.Boards.Add(board);
        database.BoardMembers.Add(NewMember(board.Id, session, nickname));
        await database.SaveChangesAsync(cancellationToken);
        return new BoardCreated(board.Id, board.JoinToken, board.Title);
    }

    public async Task<MemberView> JoinAsync(string token, JoinBoardRequest request, string session, CancellationToken cancellationToken)
    {
        var nickname = Required(request.Nickname, "Nickname", 40);
        var board = await FindBoardAsync(token, cancellationToken);
        var hash = SessionCookies.Hash(session);
        var member = await database.BoardMembers.SingleOrDefaultAsync(
            candidate => candidate.BoardId == board.Id && candidate.SessionHash == hash, cancellationToken);

        if (member is null)
        {
            member = NewMember(board.Id, session, nickname);
            database.BoardMembers.Add(member);
        }
        else
        {
            member.Nickname = nickname;
        }

        await database.SaveChangesAsync(cancellationToken);
        return Member(member);
    }

    public async Task<BoardSnapshot> SnapshotAsync(string token, string session, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var board = await AuthorizeAsync(token, session, cancellationToken);
        var columns = await database.Columns.AsNoTracking()
            .Where(column => column.BoardId == board.Id && !column.IsArchived)
            .OrderBy(column => column.Rank).ThenBy(column => column.Id)
            .ToListAsync(cancellationToken);
        var columnIds = columns.Select(column => column.Id).ToArray();
        var cards = await database.Cards.AsNoTracking()
            .Where(card => columnIds.Contains(card.ColumnId) && !card.IsArchived)
            .OrderBy(card => card.Rank).ThenBy(card => card.Id)
            .ToListAsync(cancellationToken);
        var members = await database.BoardMembers.AsNoTracking()
            .Where(member => member.BoardId == board.Id)
            .OrderBy(member => member.Id)
            .Select(member => new MemberView(member.Id, member.Nickname, member.Colour))
            .ToListAsync(cancellationToken);

        var snapshot = new BoardSnapshot(board.Id, board.Title, board.JoinToken,
            columns.Select(column => new ColumnView(column.Id, column.Title, column.Rank,
                cards.Where(card => card.ColumnId == column.Id).Select(Card).ToArray())).ToArray(), members);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task<BoardSnapshot> RenameBoardAsync(string token, string session, RenameBoardRequest request, CancellationToken cancellationToken)
    {
        var board = await AuthorizeAsync(token, session, cancellationToken);
        board.Title = Required(request.Title, "Board title", 120);
        await database.SaveChangesAsync(cancellationToken);
        return await SnapshotAsync(token, session, cancellationToken);
    }

    public async Task<ColumnView> CreateColumnAsync(string token, string session, CreateColumnRequest request, CancellationToken cancellationToken)
    {
        var board = await AuthorizeAsync(token, session, cancellationToken);
        var title = Required(request.Title, "Column title", 120);
        var previous = await ColumnRankAsync(board.Id, request.PreviousColumnId, cancellationToken);
        var next = await ColumnRankAsync(board.Id, request.NextColumnId, cancellationToken);
        if (request.PreviousColumnId is null && request.NextColumnId is null)
        {
            previous = await database.Columns.Where(column => column.BoardId == board.Id && !column.IsArchived)
                .OrderByDescending(column => column.Rank).ThenByDescending(column => column.Id)
                .Select(column => column.Rank).FirstOrDefaultAsync(cancellationToken);
        }

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Title = title,
            Rank = Between(previous, next)
        };
        database.Columns.Add(column);
        await database.SaveChangesAsync(cancellationToken);
        return new ColumnView(column.Id, column.Title, column.Rank, []);
    }

    public async Task<ColumnView> RenameColumnAsync(string token, string session, Guid columnId, RenameColumnRequest request, CancellationToken cancellationToken)
    {
        var board = await AuthorizeAsync(token, session, cancellationToken);
        var column = await RequireColumnAsync(board.Id, columnId, cancellationToken);
        column.Title = Required(request.Title, "Column title", 120);
        await database.SaveChangesAsync(cancellationToken);
        return new ColumnView(column.Id, column.Title, column.Rank, []);
    }

    public async Task<CardView> CreateCardAsync(string token, string session, Guid columnId, CreateCardRequest request, CancellationToken cancellationToken)
    {
        var board = await AuthorizeAsync(token, session, cancellationToken);
        await RequireColumnAsync(board.Id, columnId, cancellationToken);
        var title = Required(request.Title, "Card title", 160);
        var description = Optional(request.Description, "Description", 4000);
        var previous = await CardRankAsync(columnId, request.PreviousCardId, cancellationToken);
        var next = await CardRankAsync(columnId, request.NextCardId, cancellationToken);
        if (request.PreviousCardId is null && request.NextCardId is null)
        {
            previous = await database.Cards.Where(card => card.ColumnId == columnId && !card.IsArchived)
                .OrderByDescending(card => card.Rank).ThenByDescending(card => card.Id)
                .Select(card => card.Rank).FirstOrDefaultAsync(cancellationToken);
        }

        var card = new Card
        {
            Id = Guid.NewGuid(), ColumnId = columnId, Title = title, Description = description,
            Rank = Between(previous, next), Version = 1
        };
        database.Cards.Add(card);
        await database.SaveChangesAsync(cancellationToken);
        return Card(card);
    }

    public async Task<CardView> EditCardAsync(string token, string session, Guid cardId, EditCardRequest request, CancellationToken cancellationToken)
    {
        var board = await AuthorizeAsync(token, session, cancellationToken);
        var card = await RequireCardAsync(board.Id, cardId, cancellationToken);
        CheckVersion(card, request.Version);
        card.Title = Required(request.Title, "Card title", 160);
        card.Description = Optional(request.Description, "Description", 4000);
        card.Version++;
        await SaveCardAsync(cancellationToken);
        return Card(card);
    }

    public async Task<CardView> MoveCardAsync(string token, string session, Guid cardId, MoveCardRequest request, CancellationToken cancellationToken)
    {
        var board = await AuthorizeAsync(token, session, cancellationToken);
        var card = await RequireCardAsync(board.Id, cardId, cancellationToken);
        CheckVersion(card, request.Version);
        await RequireColumnAsync(board.Id, request.NewColumnId, cancellationToken);
        if (request.PreviousCardId == cardId || request.NextCardId == cardId)
        {
            throw new BoardProblem(400, "A card cannot be its own neighbour.");
        }

        var previous = await CardRankAsync(request.NewColumnId, request.PreviousCardId, cancellationToken);
        var next = await CardRankAsync(request.NewColumnId, request.NextCardId, cancellationToken);
        if (request.PreviousCardId is null && request.NextCardId is null)
        {
            previous = await database.Cards.Where(candidate => candidate.ColumnId == request.NewColumnId && !candidate.IsArchived && candidate.Id != cardId)
                .OrderByDescending(candidate => candidate.Rank).ThenByDescending(candidate => candidate.Id)
                .Select(candidate => candidate.Rank).FirstOrDefaultAsync(cancellationToken);
        }

        card.ColumnId = request.NewColumnId;
        card.Rank = Between(previous, next);
        card.Version++;
        await SaveCardAsync(cancellationToken);
        return Card(card);
    }

    public async Task<CardView> ArchiveCardAsync(string token, string session, Guid cardId, VersionRequest request, CancellationToken cancellationToken)
    {
        var board = await AuthorizeAsync(token, session, cancellationToken);
        var card = await RequireCardAsync(board.Id, cardId, cancellationToken);
        CheckVersion(card, request.Version);
        card.IsArchived = true;
        card.Version++;
        await SaveCardAsync(cancellationToken);
        return Card(card);
    }

    private async Task<Board> AuthorizeAsync(string token, string session, CancellationToken cancellationToken)
    {
        var board = await FindBoardAsync(token, cancellationToken);
        if (session.Length != 64)
        {
            throw new BoardProblem(401, "Join this board first.");
        }

        var hash = SessionCookies.Hash(session);
        if (!await database.BoardMembers.AnyAsync(member => member.BoardId == board.Id && member.SessionHash == hash, cancellationToken))
        {
            throw new BoardProblem(403, "This session is not a member of the board.");
        }

        return board;
    }

    private async Task<Board> FindBoardAsync(string token, CancellationToken cancellationToken) =>
        await database.Boards.SingleOrDefaultAsync(board => board.JoinToken == token, cancellationToken)
        ?? throw new BoardProblem(404, "Board not found.");

    private async Task<BoardColumn> RequireColumnAsync(Guid boardId, Guid columnId, CancellationToken cancellationToken) =>
        await database.Columns.SingleOrDefaultAsync(column => column.Id == columnId && column.BoardId == boardId && !column.IsArchived, cancellationToken)
        ?? throw new BoardProblem(404, "Column not found.");

    private async Task<Card> RequireCardAsync(Guid boardId, Guid cardId, CancellationToken cancellationToken) =>
        await database.Cards.Join(database.Columns, card => card.ColumnId, column => column.Id,
            (card, column) => new { card, column })
            .Where(item => item.card.Id == cardId && !item.card.IsArchived && item.column.BoardId == boardId && !item.column.IsArchived)
            .Select(item => item.card).SingleOrDefaultAsync(cancellationToken)
        ?? throw new BoardProblem(404, "Card not found.");

    private async Task<string?> ColumnRankAsync(Guid boardId, Guid? columnId, CancellationToken cancellationToken) =>
        columnId is null ? null : (await RequireColumnAsync(boardId, columnId.Value, cancellationToken)).Rank;

    private async Task<string?> CardRankAsync(Guid columnId, Guid? cardId, CancellationToken cancellationToken)
    {
        if (cardId is null) return null;
        return (await database.Cards.SingleOrDefaultAsync(card => card.Id == cardId && card.ColumnId == columnId && !card.IsArchived, cancellationToken)
            ?? throw new BoardProblem(404, "Neighbour card not found.")).Rank;
    }

    private async Task SaveCardAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BoardProblem(409, "Someone else changed this card. Reload its current version.");
        }
    }

    private static void CheckVersion(Card card, long version)
    {
        if (version != card.Version)
        {
            throw new BoardProblem(409, "Someone else changed this card. Reload its current version.");
        }
    }

    private static string Between(string? before, string? after)
    {
        try { return FractionalRank.Between(before, after); }
        catch (ArgumentException) { throw new BoardProblem(409, "The neighbours have changed. Reload the board and try again."); }
    }

    private static string Required(string? value, string name, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
            throw new BoardProblem(400, $"{name} must contain 1 to {maxLength} characters.");
        return trimmed;
    }

    private static string Optional(string? value, string name, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length > maxLength)
            throw new BoardProblem(400, $"{name} cannot exceed {maxLength} characters.");
        return trimmed;
    }

    private static BoardMember NewMember(Guid boardId, string session, string nickname) => new()
    {
        Id = Guid.NewGuid(), BoardId = boardId, SessionHash = SessionCookies.Hash(session),
        Nickname = nickname, Colour = Colours[RandomNumberGenerator.GetInt32(Colours.Length)]
    };

    private static MemberView Member(BoardMember member) => new(member.Id, member.Nickname, member.Colour);
    private static CardView Card(Card card) => new(card.Id, card.ColumnId, card.Title, card.Description, card.Rank, card.Version);
}
