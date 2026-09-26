using Cardflow.Api.Realtime;
using DotNet.Testcontainers.Builders;
using StackExchange.Redis;
using Xunit;

namespace Cardflow.Api.Tests;

public sealed class BoardPresenceTests
{
    [Fact]
    public async Task SweepRemovesOrphanAndPreservesFreshHeartbeat()
    {
        await using var container = new ContainerBuilder("redis:7-alpine")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
        await container.StartAsync();
        using var redis = await ConnectionMultiplexer.ConnectAsync($"127.0.0.1:{container.GetMappedPublicPort(6379)}");
        var presence = new BoardPresence(redis);
        var database = redis.GetDatabase();
        var boardId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        await presence.JoinAsync(boardId, new PresenceEntry("orphan", memberId, "Ada", "#112233", null));
        await presence.JoinAsync(boardId, new PresenceEntry("active", memberId, "Ada", "#112233", null));
        Assert.Equal(2, (await presence.SnapshotAsync(boardId)).Count);

        // Simulate a process disappearing without a disconnect callback.
        await database.SortedSetAddAsync($"presence:{boardId:N}", "orphan",
            DateTimeOffset.UtcNow.Subtract(BoardPresence.Expiry).AddSeconds(-1).ToUnixTimeMilliseconds());
        Assert.True(await presence.HeartbeatAsync(boardId, "active"));
        var expired = await presence.SweepAsync();

        Assert.Contains(expired, item => item.BoardId == boardId && item.ConnectionId == "orphan");
        Assert.Single(await presence.SnapshotAsync(boardId));
        Assert.False(await database.HashExistsAsync($"presence:meta:{boardId:N}", "orphan"));
        Assert.True(await database.HashExistsAsync($"presence:meta:{boardId:N}", "active"));
    }
}
