namespace Cardflow.Api.Data.Entities;

public sealed class BoardEvent
{
    public Guid BoardId { get; set; }
    public long Seq { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public Guid? ActorMemberId { get; set; }
    public DateTime CreatedAt { get; set; }
}
