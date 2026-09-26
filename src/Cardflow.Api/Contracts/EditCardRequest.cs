namespace Cardflow.Api.Contracts;

public sealed record EditCardRequest(string Title, string Description, long Version);
