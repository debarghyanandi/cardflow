namespace Cardflow.Api.Contracts;

public sealed record PresenceEntry(string ConnectionId, Guid MemberId, string Nickname, string Colour, Guid? EditingCardId);
