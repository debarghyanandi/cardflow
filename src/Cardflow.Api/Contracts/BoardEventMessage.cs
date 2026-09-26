using System.Text.Json;

namespace Cardflow.Api.Contracts;

public sealed record BoardEventMessage(Guid BoardId, long Seq, string Type, JsonElement Payload, DateTime CreatedAt);
