namespace Cardflow.Api.Contracts;

public sealed record CursorPosition(string ConnectionId, Guid MemberId, string Nickname, string Colour, double X, double Y);
