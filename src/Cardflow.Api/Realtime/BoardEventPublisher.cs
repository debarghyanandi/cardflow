using Cardflow.Api.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Cardflow.Api.Realtime;

public sealed class BoardEventPublisher(IHubContext<BoardHub> hub, ILogger<BoardEventPublisher> logger)
{
    public async Task PublishAsync(BoardEventMessage boardEvent)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await hub.Clients.Group(BoardHub.GroupName(boardEvent.BoardId))
                .SendAsync("BoardEvent", boardEvent, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Broadcast of board {BoardId} event {Seq} failed; clients must catch up from the event log",
                boardEvent.BoardId, boardEvent.Seq);
        }
    }
}
