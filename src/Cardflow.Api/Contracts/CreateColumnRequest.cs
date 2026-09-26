namespace Cardflow.Api.Contracts;

public sealed record CreateColumnRequest(string Title, Guid? PreviousColumnId, Guid? NextColumnId);
