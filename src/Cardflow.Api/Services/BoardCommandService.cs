using Cardflow.Api.Contracts;
using Cardflow.Api.Data;
using Cardflow.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Cardflow.Api.Services;

public sealed class BoardCommandService(
    CardflowDbContext database, BoardService boardService, BoardEventStore events)
{
    public Task<BoardMutation<BoardCreated>> CreateBoardAsync(
        CreateBoardRequest request, string session, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.CreateBoardAsync(request, session, cancellationToken),
            value => value.Token, session, "BoardCreated", cancellationToken,
            value => new { value.Id, value.Title });

    public Task<BoardMutation<MemberView>> JoinAsync(
        string token, JoinBoardRequest request, string session, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.JoinAsync(token, request, session, cancellationToken),
            _ => token, session, "MemberJoined", cancellationToken);

    public Task<BoardMutation<BoardSnapshot>> RenameBoardAsync(
        string token, string session, RenameBoardRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.RenameBoardAsync(token, session, request, cancellationToken),
            _ => token, session, "BoardRenamed", cancellationToken,
            value => new { value.Id, value.Title });

    public Task<BoardMutation<ColumnView>> CreateColumnAsync(
        string token, string session, CreateColumnRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.CreateColumnAsync(token, session, request, cancellationToken),
            _ => token, session, "ColumnCreated", cancellationToken);

    public Task<BoardMutation<ColumnView>> RenameColumnAsync(
        string token, string session, Guid columnId, RenameColumnRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.RenameColumnAsync(token, session, columnId, request, cancellationToken),
            _ => token, session, "ColumnRenamed", cancellationToken);

    public Task<BoardMutation<CardView>> CreateCardAsync(
        string token, string session, Guid columnId, CreateCardRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.CreateCardAsync(token, session, columnId, request, cancellationToken),
            _ => token, session, "CardCreated", cancellationToken);

    public Task<BoardMutation<CardView>> EditCardAsync(
        string token, string session, Guid cardId, EditCardRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.EditCardAsync(token, session, cardId, request, cancellationToken),
            _ => token, session, "CardEdited", cancellationToken);

    public Task<BoardMutation<CardView>> MoveCardAsync(
        string token, string session, Guid cardId, MoveCardRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.MoveCardAsync(token, session, cardId, request, cancellationToken),
            _ => token, session, "CardMoved", cancellationToken);

    public Task<BoardMutation<CardView>> ArchiveCardAsync(
        string token, string session, Guid cardId, VersionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(() => boardService.ArchiveCardAsync(token, session, cardId, request, cancellationToken),
            _ => token, session, "CardArchived", cancellationToken);

    private async Task<BoardMutation<T>> ExecuteAsync<T>(
        Func<Task<T>> change, Func<T, string> tokenFor, string session, string type,
        CancellationToken cancellationToken, Func<T, object>? payloadFor = null)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var value = await change();
        var token = tokenFor(value);
        var board = await database.Boards.AsNoTracking()
            .SingleAsync(candidate => candidate.JoinToken == token, cancellationToken);
        var sessionHash = SessionCookieHelper.Hash(session);
        var actorId = await database.BoardMembers.AsNoTracking()
            .Where(member => member.BoardId == board.Id && member.SessionHash == sessionHash)
            .Select(member => member.Id).SingleAsync(cancellationToken);
        var boardEvent = await events.AppendAsync(board.Id, actorId, type, payloadFor?.Invoke(value) ?? value!, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BoardMutation<T>(value, boardEvent);
    }
}
