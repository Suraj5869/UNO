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
                        if (game is null) return (Resolved: false, Action: (GameActionEvent?)null);

                        var currentGamePlayerId = game.CurrentPlayer.Id;
                        var before = game.CurrentPlayer.Hand.Count;
                        if (!new UnoGameEngine().AutoResolveTimedOutTurn(game, DateTimeOffset.UtcNow))
                            return (Resolved: false, Action: (GameActionEvent?)null);

                        var drawCount = game.Players.First(player => player.Id == currentGamePlayerId).Hand.Count - before;
                        if (drawCount == 0) return (Resolved: true, Action: (GameActionEvent?)null);
                        var roomPlayerId = room.Players.First(player => player.GamePlayerId == currentGamePlayerId).Id;
                        return (Resolved: true, Action: (GameActionEvent?)publisher.CaptureGameAction(code, new GameActionPayload("CardsDrawn", roomPlayerId, null, drawCount)));
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
