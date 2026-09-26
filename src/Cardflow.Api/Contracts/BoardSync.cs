namespace Cardflow.Api.Contracts;

public sealed record BoardSync(long Seq, BoardSnapshot? Snapshot, IReadOnlyList<BoardEventMessage> Events);
