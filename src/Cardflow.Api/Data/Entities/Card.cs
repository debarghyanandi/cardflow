namespace Cardflow.Api.Data.Entities;

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
