using System.Text.Json;
using Cardflow.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cardflow.Api.Boards;

public sealed record BoardSync(long Seq, BoardSnapshot? Snapshot, IReadOnlyList<BoardEventMessage> Events);

public sealed class BoardSyncService(CardflowDbContext database, BoardService boards)
{
    public const int ReplayLimit = 500;

    public async Task<BoardSync> CatchUpAsync(
        string token, string session, long afterSeq, CancellationToken cancellationToken)
    {
        // The snapshot both checks membership and gives us a consistent high-water mark.
        var snapshot = await boards.SnapshotAsync(token, session, cancellationToken);
        if (afterSeq < 0 || afterSeq > snapshot.Seq || snapshot.Seq - afterSeq > ReplayLimit)
            return new BoardSync(snapshot.Seq, snapshot, []);

        var rows = await database.BoardEvents.AsNoTracking()
            .Where(boardEvent => boardEvent.BoardId == snapshot.Id &&
                boardEvent.Seq > afterSeq && boardEvent.Seq <= snapshot.Seq)
            .OrderBy(boardEvent => boardEvent.Seq)
            .ToListAsync(cancellationToken);

        // A pruned or otherwise missing event makes replay unsafe.
        if (rows.Count != snapshot.Seq - afterSeq ||
            rows.Where((row, index) => row.Seq != afterSeq + index + 1).Any())
            return new BoardSync(snapshot.Seq, snapshot, []);

        var events = rows.Select(row => new BoardEventMessage(row.BoardId, row.Seq,
            row.Type, JsonSerializer.Deserialize<JsonElement>(row.Payload), row.CreatedAt)).ToArray();
        return new BoardSync(snapshot.Seq, null, events);
    }
}
