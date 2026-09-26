namespace Cardflow.Api.Boards;

public sealed record CreateBoardRequest(string Title, string Nickname);
public sealed record JoinBoardRequest(string Nickname);
public sealed record RenameBoardRequest(string Title);
public sealed record CreateColumnRequest(string Title, Guid? PreviousColumnId, Guid? NextColumnId);
public sealed record RenameColumnRequest(string Title);
public sealed record CreateCardRequest(string Title, string Description, Guid? PreviousCardId, Guid? NextCardId);
public sealed record EditCardRequest(string Title, string Description, long Version);
public sealed record MoveCardRequest(Guid NewColumnId, Guid? PreviousCardId, Guid? NextCardId, long Version);
public sealed record VersionRequest(long Version);

public sealed record BoardCreated(Guid Id, string Token, string Title);
public sealed record MemberView(Guid Id, string Nickname, string Colour);
public sealed record CardView(Guid Id, Guid ColumnId, string Title, string Description, string Rank, long Version);
public sealed record ColumnView(Guid Id, string Title, string Rank, IReadOnlyList<CardView> Cards);
public sealed record BoardSnapshot(Guid Id, string Title, string Token, IReadOnlyList<ColumnView> Columns, IReadOnlyList<MemberView> Members, long Seq);

public sealed class BoardProblem(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
