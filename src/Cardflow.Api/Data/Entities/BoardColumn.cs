namespace Cardflow.Api.Data.Entities;

public sealed class BoardColumn
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public string Title { get; set; } = "";
    public string Rank { get; set; } = "";
    public bool IsArchived { get; set; }
}
