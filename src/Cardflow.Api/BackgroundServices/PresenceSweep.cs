using Cardflow.Api.Providers;
using Cardflow.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Cardflow.Api.BackgroundServices;

public sealed class PresenceSweep(BoardPresenceProvider presence, IHubContext<BoardHub> hubs, ILogger<PresenceSweep> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var (boardId, connectionId) in await presence.SweepAsync())
                    await hubs.Clients.Group(BoardHub.GroupName(boardId)).SendAsync("PresenceLeft", connectionId, stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Presence sweep failed; the next pass will retry");
            }
        }
    }
}
