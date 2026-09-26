using Cardflow.Api.Boards;
using Microsoft.AspNetCore.SignalR;

namespace Cardflow.Api.Realtime;

public sealed class BoardHub(BoardService boards, BoardCommandService commands, BoardEventPublisher publisher) : Hub
{
    public static string GroupName(Guid boardId) => $"board:{boardId:N}";

    public async Task<BoardSnapshot> JoinBoard(string token)
    {
        var snapshot = await CallAsync(() => boards.SnapshotAsync(token, Session(), Context.ConnectionAborted));
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(snapshot.Id), Context.ConnectionAborted);
        return snapshot;
    }

    public Task<CardView> MoveCard(string token, Guid cardId, MoveCardRequest request) =>
        ChangeAsync(() => commands.MoveCardAsync(token, Session(), cardId, request, Context.ConnectionAborted));

    public Task<CardView> EditCard(string token, Guid cardId, EditCardRequest request) =>
        ChangeAsync(() => commands.EditCardAsync(token, Session(), cardId, request, Context.ConnectionAborted));

    public Task<CardView> ArchiveCard(string token, Guid cardId, VersionRequest request) =>
        ChangeAsync(() => commands.ArchiveCardAsync(token, Session(), cardId, request, Context.ConnectionAborted));

    public Task<CardView> CreateCard(string token, Guid columnId, CreateCardRequest request) =>
        ChangeAsync(() => commands.CreateCardAsync(token, Session(), columnId, request, Context.ConnectionAborted));

    public Task<ColumnView> CreateColumn(string token, CreateColumnRequest request) =>
        ChangeAsync(() => commands.CreateColumnAsync(token, Session(), request, Context.ConnectionAborted));

    public Task<ColumnView> RenameColumn(string token, Guid columnId, RenameColumnRequest request) =>
        ChangeAsync(() => commands.RenameColumnAsync(token, Session(), columnId, request, Context.ConnectionAborted));

    public Task<BoardSnapshot> RenameBoard(string token, RenameBoardRequest request) =>
        ChangeAsync(() => commands.RenameBoardAsync(token, Session(), request, Context.ConnectionAborted));

    private string Session() => SessionCookies.Existing(Context.GetHttpContext()
        ?? throw new HubException("HTTP context is unavailable."));

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (BoardProblem problem) { throw new HubException($"{problem.StatusCode}: {problem.Message}"); }
    }

    private async Task<T> ChangeAsync<T>(Func<Task<BoardMutation<T>>> change)
    {
        var mutation = await CallAsync(change);
        await publisher.PublishAsync(mutation.Event);
        return mutation.Value;
    }
}
