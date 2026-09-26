namespace Cardflow.Api.Data.Entities;

public sealed class Board
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string JoinToken { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long EventSeq { get; set; }
}
