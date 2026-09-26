using System.Text.Json;
using Cardflow.Api.Contracts;
using StackExchange.Redis;

namespace Cardflow.Api.Providers;

/// <summary>
/// The Redis-backed wrapper around who is currently on a board — a sorted set
/// keyed by last-seen timestamp (so an unresponsive member ages out on its
/// own) plus a hash of each member's display info. This is the same shape as
/// your reference project's <c>Providers/</c> folder: a thin wrapper around
/// one external store (there DynamoDB, here Redis), with the DI-facing
/// naming (<c>*Provider</c>) to match.
/// </summary>
public sealed class BoardPresenceProvider(IConnectionMultiplexer redis)
{
    public static readonly TimeSpan Expiry = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string BoardsKey = "presence:boards";
    private static RedisKey SeenKey(Guid boardId) => $"presence:{boardId:N}";
    private static RedisKey MetaKey(Guid boardId) => $"presence:meta:{boardId:N}";
    private IDatabase Db => redis.GetDatabase();

    public async Task<IReadOnlyList<PresenceEntry>> JoinAsync(Guid boardId, PresenceEntry entry)
    {
        await Db.ScriptEvaluateAsync("""
            redis.call('SADD', KEYS[1], ARGV[1])
            redis.call('HSET', KEYS[2], ARGV[2], ARGV[3])
            redis.call('ZADD', KEYS[3], ARGV[4], ARGV[2])
            return 1
            """, [(RedisKey)BoardsKey, MetaKey(boardId), SeenKey(boardId)],
            [boardId.ToString("N"), entry.ConnectionId, JsonSerializer.Serialize(entry, Json), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()]);
        return await SnapshotAsync(boardId);
    }

    public async Task<IReadOnlyList<PresenceEntry>> SnapshotAsync(Guid boardId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var live = await Db.SortedSetRangeByScoreAsync(SeenKey(boardId), now - Expiry.TotalMilliseconds, double.PositiveInfinity);
        if (live.Length == 0) return [];
        var values = await Db.HashGetAsync(MetaKey(boardId), live.Select(id => (RedisValue)id.ToString()).ToArray());
        return values.Where(value => value.HasValue)
            .Select(value => JsonSerializer.Deserialize<PresenceEntry>(value.ToString(), Json)!)
            .ToArray();
    }

    public async Task<bool> HeartbeatAsync(Guid boardId, string connectionId)
    {
        var updated = (long)await Db.ScriptEvaluateAsync("""
            if redis.call('HEXISTS', KEYS[1], ARGV[1]) == 0 then return 0 end
            redis.call('ZADD', KEYS[2], ARGV[2], ARGV[1])
            return 1
            """, [MetaKey(boardId), SeenKey(boardId)], [connectionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()]);
        return updated == 1;
    }

    public async Task<PresenceEntry?> SetEditingAsync(Guid boardId, string connectionId, Guid? cardId)
    {
        var value = await Db.HashGetAsync(MetaKey(boardId), connectionId);
        if (!value.HasValue) return null;
        var entry = JsonSerializer.Deserialize<PresenceEntry>(value.ToString(), Json)! with { EditingCardId = cardId };
        var updated = (long)await Db.ScriptEvaluateAsync("""
            if not redis.call('ZSCORE', KEYS[1], ARGV[1]) then return 0 end
            redis.call('HSET', KEYS[2], ARGV[1], ARGV[2])
            return 1
            """, [SeenKey(boardId), MetaKey(boardId)], [connectionId, JsonSerializer.Serialize(entry, Json)]);
        return updated == 1 ? entry : null;
    }

    public async Task<bool> LeaveAsync(Guid boardId, string connectionId)
    {
        var removed = await Db.SortedSetRemoveAsync(SeenKey(boardId), connectionId);
        await Db.HashDeleteAsync(MetaKey(boardId), connectionId);
        return removed;
    }

    public async Task<IReadOnlyList<(Guid BoardId, string ConnectionId)>> SweepAsync()
    {
        var expired = new List<(Guid, string)>();
        foreach (var boardValue in await Db.SetMembersAsync(BoardsKey))
        {
            if (!Guid.TryParseExact(boardValue.ToString(), "N", out var boardId)) continue;
            var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Expiry.TotalMilliseconds;
            var stale = await Db.SortedSetRangeByScoreAsync(SeenKey(boardId), double.NegativeInfinity, cutoff);
            foreach (var connectionId in stale)
            {
                // The script rechecks the score: a heartbeat between reading and removing wins.
                var removed = (long)await Db.ScriptEvaluateAsync("""
                    local score = redis.call('ZSCORE', KEYS[1], ARGV[1])
                    if not score or tonumber(score) > tonumber(ARGV[2]) then return 0 end
                    redis.call('ZREM', KEYS[1], ARGV[1])
                    redis.call('HDEL', KEYS[2], ARGV[1])
                    return 1
                    """, [SeenKey(boardId), MetaKey(boardId)], [connectionId, cutoff]);
                if (removed == 1) expired.Add((boardId, connectionId.ToString()));
            }
            // Registry cleanup is safe only when the board has no current connections.
            await Db.ScriptEvaluateAsync("""
                if redis.call('ZCARD', KEYS[1]) == 0 then redis.call('SREM', KEYS[2], ARGV[1]) end
                return 1
                """, [SeenKey(boardId), (RedisKey)BoardsKey], [boardValue]);
        }
        return expired;
    }
}
