namespace Cardflow.Api.Contracts;

public sealed record CreateCardRequest(string Title, string Description, Guid? PreviousCardId, Guid? NextCardId);
