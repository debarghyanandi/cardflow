namespace Cardflow.Api.Contracts;

public sealed record BoardMutation<T>(T Value, BoardEventMessage Event);
