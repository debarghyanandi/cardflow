namespace Cardflow.Api.Contracts;

public sealed record ColumnView(Guid Id, string Title, string Rank, IReadOnlyList<CardView> Cards);
