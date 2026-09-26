using Cardflow.Api.Boards;
using Microsoft.AspNetCore.SignalR;

namespace Cardflow.Api.Realtime;

public sealed class BoardHub(BoardService boards, BoardSyncService sync, BoardCommandService commands, BoardEventPublisher publisher, BoardPresence presence) : Hub
{
    private const string BoardContextKey = "presence-board";
    private const string TokenContextKey = "presence-token";
    private const string MemberContextKey = "presence-member";
    public static string GroupName(Guid boardId) => $"board:{boardId:N}";

    public async Task<BoardSnapshot> JoinBoard(string token)
    {
        var snapshot = await CallAsync(() => boards.SnapshotAsync(token, Session(), Context.ConnectionAborted));
        if (Context.Items.TryGetValue(BoardContextKey, out var previous) && !Equals(previous, snapshot.Id))
            throw new HubException("A connection may join only one board.");
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(snapshot.Id), Context.ConnectionAborted);
        Context.Items[BoardContextKey] = snapshot.Id;
        var member = await CallAsync(() => boards.PresenceMemberAsync(token, Session(), Context.ConnectionAborted));
        Context.Items[TokenContextKey] = token;
        Context.Items[MemberContextKey] = member.Member;
        var entry = new PresenceEntry(Context.ConnectionId, member.Member.Id, member.Member.Nickname, member.Member.Colour, null);
        var members = await presence.JoinAsync(snapshot.Id, entry);
        await Clients.Caller.SendAsync("PresenceSnapshot", members, Context.ConnectionAborted);
        await Clients.OthersInGroup(GroupName(snapshot.Id)).SendAsync("PresenceChanged", entry, Context.ConnectionAborted);
        return snapshot;
    }

    public async Task Heartbeat(string token)
    {
        var (boardId, _) = JoinedPresence(token);
        if (!await presence.HeartbeatAsync(boardId, Context.ConnectionId))
            throw new HubException("Join the board again before sending presence.");
    }

    public async Task MoveCursor(string token, double x, double y)
    {
        var (boardId, member) = JoinedPresence(token);
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1)
            throw new HubException("Cursor coordinates must be between 0 and 1.");
        await Clients.OthersInGroup(GroupName(boardId)).SendAsync("CursorMoved",
            new CursorPosition(Context.ConnectionId, member.Id, member.Nickname, member.Colour, x, y), Context.ConnectionAborted);
    }

    public async Task SetEditing(string token, Guid? cardId)
    {
        var member = await AuthorizedPresenceAsync(token);
        if (cardId is not null)
        {
            var snapshot = await CallAsync(() => boards.SnapshotAsync(token, Session(), Context.ConnectionAborted));
            if (!snapshot.Columns.Any(column => column.Cards.Any(card => card.Id == cardId)))
                throw new HubException("Card not found on this board.");
        }
        if (!await presence.HeartbeatAsync(member.BoardId, Context.ConnectionId))
            throw new HubException("Join the board again before editing.");
        var entry = await presence.SetEditingAsync(member.BoardId, Context.ConnectionId, cardId);
        if (entry is not null)
            await Clients.Group(GroupName(member.BoardId)).SendAsync("PresenceChanged", entry, Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(BoardContextKey, out var value) && value is Guid boardId &&
            await presence.LeaveAsync(boardId, Context.ConnectionId))
            await Clients.Group(GroupName(boardId)).SendAsync("PresenceLeft", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task<BoardSync> CatchUp(string token, long afterSeq) =>
        CallAsync(() => sync.CatchUpAsync(token, Session(), afterSeq, Context.ConnectionAborted));

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

    private async Task<(Guid BoardId, MemberView Member)> AuthorizedPresenceAsync(string token)
    {
        JoinedPresence(token);
        var result = await CallAsync(() => boards.PresenceMemberAsync(token, Session(), Context.ConnectionAborted));
        if (!Context.Items.TryGetValue(BoardContextKey, out var value) || !Equals(value, result.BoardId))
            throw new HubException("Join this board on this connection first.");
        return result;
    }

    private (Guid BoardId, MemberView Member) JoinedPresence(string token)
    {
        if (!Context.Items.TryGetValue(BoardContextKey, out var board) || board is not Guid boardId ||
            !Context.Items.TryGetValue(TokenContextKey, out var joinedToken) || !Equals(joinedToken, token) ||
            !Context.Items.TryGetValue(MemberContextKey, out var member) || member is not MemberView memberView)
            throw new HubException("Join this board on this connection first.");
        return (boardId, memberView);
    }

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
