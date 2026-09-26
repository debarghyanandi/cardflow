namespace Cardflow.Api.Contracts;

public sealed record MoveCardRequest(Guid NewColumnId, Guid? PreviousCardId, Guid? NextCardId, long Version);
