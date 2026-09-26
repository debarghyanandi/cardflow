namespace Cardflow.Api.Data;

public sealed class Board
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string JoinToken { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public long EventSeq { get; set; }
}

public sealed class BoardMember
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public string SessionHash { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Colour { get; set; } = "";
}

public sealed class BoardColumn
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public string Title { get; set; } = "";
    public string Rank { get; set; } = "";
    public bool IsArchived { get; set; }
}

public sealed class Card
{
    public Guid Id { get; set; }
    public Guid ColumnId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Rank { get; set; } = "";
    public long Version { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class BoardEvent
{
    public Guid BoardId { get; set; }
    public long Seq { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public Guid? ActorMemberId { get; set; }
    public DateTime CreatedAt { get; set; }
}
