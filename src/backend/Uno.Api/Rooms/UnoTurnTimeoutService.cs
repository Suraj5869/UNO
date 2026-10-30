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
                    var timeoutAction = roomStore.ExecuteLocked(code, room =>
                    {
                        var game = room.Game;
                        if (game is null) return (Resolved: false, Action: (GameActionPayload?)null);

                        var currentGamePlayerId = game.CurrentPlayer.Id;
                        var drawCount = game.PendingDrawAmount > 0 ? game.PendingDrawAmount : 1;
                        if (!new UnoGameEngine().AutoResolveTimedOutTurn(game, DateTimeOffset.UtcNow))
                            return (Resolved: false, Action: (GameActionPayload?)null);

                        var roomPlayerId = room.Players.First(player => player.GamePlayerId == currentGamePlayerId).Id;
                        return (Resolved: true, Action: (GameActionPayload?)new GameActionPayload("CardsDrawn", roomPlayerId, null, drawCount));
                    });
                    if (timeoutAction.Resolved)
                    {
                        if (timeoutAction.Action is not null)
                            await publisher.PublishGameActionAsync(code, timeoutAction.Action);
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
