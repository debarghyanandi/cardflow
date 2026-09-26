namespace Cardflow.Api.Contracts;

public sealed record BoardSnapshot(Guid Id, string Title, string Token, IReadOnlyList<ColumnView> Columns, IReadOnlyList<MemberView> Members, long Seq);
