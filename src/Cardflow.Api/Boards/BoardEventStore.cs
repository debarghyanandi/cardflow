using System.Text.Json;
using Cardflow.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Cardflow.Api.Boards;

public sealed record BoardEventMessage(Guid BoardId, long Seq, string Type, JsonElement Payload, DateTime CreatedAt);
public sealed record BoardMutation<T>(T Value, BoardEventMessage Event);

public sealed class BoardEventStore(CardflowDbContext database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BoardEventMessage> AppendAsync(
        Guid boardId, Guid? actorMemberId, string type, object payload, CancellationToken cancellationToken)
    {
        var transaction = database.Database.CurrentTransaction
            ?? throw new InvalidOperationException("An event must be written inside its state-change transaction.");
        var connection = (NpgsqlConnection)database.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "UPDATE boards SET event_seq = event_seq + 1 WHERE id = @boardId RETURNING event_seq",
            connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("boardId", boardId);
        var sequence = (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Board disappeared during event write."));

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var createdAt = DateTime.UtcNow;
        database.BoardEvents.Add(new BoardEvent
        {
            BoardId = boardId,
            Seq = sequence,
            Type = type,
            Payload = json,
            ActorMemberId = actorMemberId,
            CreatedAt = createdAt
        });
        await database.SaveChangesAsync(cancellationToken);

        foreach (var entry in database.ChangeTracker.Entries<Board>()
            .Where(entry => entry.Entity.Id == boardId))
        {
            entry.Property(board => board.EventSeq).CurrentValue = sequence;
            entry.Property(board => board.EventSeq).OriginalValue = sequence;
        }

        return new BoardEventMessage(boardId, sequence, type,
            JsonSerializer.Deserialize<JsonElement>(json), createdAt);
    }
}
