namespace Cardflow.Api.Data.Entities;

public sealed class BoardMember
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public string SessionHash { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Colour { get; set; } = "";
}
