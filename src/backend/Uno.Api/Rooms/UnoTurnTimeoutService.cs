using Uno.GameEngine;
namespace Uno.Api.Rooms;
public sealed class UnoTurnTimeoutService(RoomStore roomStore, RoomRealtimePublisher publisher, ILogger<UnoTurnTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var code in roomStore.GetActiveRoomCodes())
            {
                try
                {
                    var timedOut = roomStore.ExecuteLocked(code, room =>
                        room.Game is not null && new UnoGameEngine().AutoResolveTimedOutTurn(room.Game, DateTimeOffset.UtcNow));
                    if (timedOut)
                    {
                        await publisher.PublishRoomAsync(code);
                        await publisher.PublishGameStateAsync(code);
                    }
                }
                catch (KeyNotFoundException) { }
                catch (Exception exception) { logger.LogError(exception, "Failed to process turn timeout for room {RoomCode}", code); }
            }
        }
    }
}
