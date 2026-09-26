namespace Cardflow.Api.Contracts;

public sealed record CardView(Guid Id, Guid ColumnId, string Title, string Description, string Rank, long Version);
