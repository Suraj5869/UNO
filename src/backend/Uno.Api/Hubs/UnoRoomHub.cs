using Microsoft.AspNetCore.SignalR;
using Uno.Api.Rooms;
using Uno.GameEngine;

namespace Uno.Api.Hubs;

/// <summary>
/// Real-time room connection and server-authoritative game actions.
/// Every action is authorized with the participant's room access token.
/// </summary>
public sealed class UnoRoomHub(RoomStore roomStore, RoomRealtimePublisher publisher) : Hub
{
    public async Task<RoomResponse> JoinRoom(string code, string accessToken)
    {
        var joined = roomStore.ExecuteLocked(code, room =>
        {
            var playerId = roomStore.AuthorizeParticipant(code, accessToken);
            var gameState = room.Game is null
                ? null
                : GameStateResponse.From(room, playerId);
            return (PlayerId: playerId, Room: RoomResponse.From(room), GameState: gameState);
        });

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            RoomRealtimePublisher.PlayerGroup(code, joined.PlayerId));
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            RoomRealtimePublisher.RoomGroup(code));

        await Clients.Group(RoomRealtimePublisher.RoomGroup(code))
            .SendAsync("RoomUpdated", joined.Room);

        if (joined.GameState is not null)
        {
            await Clients.Caller.SendAsync("GameStateUpdated", joined.GameState);
        }

        return joined.Room;
    }

    public Task<GameStateResponse> GetGameState(string code, string accessToken)
    {
        return Task.FromResult(roomStore.ExecuteLocked(code, room =>
        {
            var playerId = roomStore.AuthorizeParticipant(code, accessToken);
            return GameStateResponse.From(room, playerId);
        }));
    }

    public Task PlayCard(
        string code,
        string accessToken,
        int cardIndex,
        CardColor chosenColor = CardColor.None)
    {
        UnoCard? playedCard = null;
        Guid? automaticDrawTargetPlayerId = null;
        var automaticDrawCount = 0;
        return ExecuteAction(code, accessToken, (room, playerId) =>
        {
            var game = RequireGame(room);
            var gamePlayerId = GetGamePlayerId(room, playerId);
            var hand = game.Players.First(player => player.Id == gamePlayerId).Hand;
            if (cardIndex < 0 || cardIndex >= hand.Count)
                throw new ArgumentOutOfRangeException(nameof(cardIndex), "Card index is outside the player's hand.");

            var card = hand[cardIndex];
            playedCard = card;
            if (card.Type == CardType.DrawTwo && game.RuleSet == UnoRuleSet.Official)
            {
                var nextPlayerIndex = (game.CurrentPlayerIndex + game.TurnDirection + game.Players.Count) % game.Players.Count;
                var nextGamePlayerId = game.Players[nextPlayerIndex].Id;
                automaticDrawTargetPlayerId = room.Players.First(player => player.GamePlayerId == nextGamePlayerId).Id;
                automaticDrawCount = 2;
            }

            new UnoGameEngine().PlayCard(game, gamePlayerId, card, chosenColor);
        }, playerId => playedCard is null
            ? null
            : new GameActionPayload("CardPlayed", playerId, UnoCardResponse.From(playedCard), automaticDrawCount, automaticDrawTargetPlayerId));
    }

    public Task DrawCard(string code, string accessToken) =>
        ExecuteAction(code, accessToken, (room, playerId) =>
            new UnoGameEngine().DrawCard(RequireGame(room), GetGamePlayerId(room, playerId)),
            playerId => new GameActionPayload("CardDrawn", playerId, null, 1));

    public Task PassAfterDraw(string code, string accessToken) =>
        ExecuteAction(code, accessToken, (room, playerId) =>
            new UnoGameEngine().PassAfterDraw(RequireGame(room), GetGamePlayerId(room, playerId)));

    public Task AcceptDrawPenalty(string code, string accessToken)
    {
        var drawCount = 0;
        return ExecuteAction(code, accessToken, (room, playerId) =>
        {
            var game = RequireGame(room);
            drawCount = game.PendingDrawAmount;
            new UnoGameEngine().AcceptDrawPenalty(game, GetGamePlayerId(room, playerId));
        }, playerId => new GameActionPayload("CardsDrawn", playerId, null, drawCount));
    }

    public Task AcceptWildDrawFour(string code, string accessToken)
    {
        var drawCount = 0;
        return ExecuteAction(code, accessToken, (room, playerId) =>
        {
            var game = RequireGame(room);
            drawCount = game.PendingDrawAmount;
            new UnoGameEngine().AcceptWildDrawFour(game, GetGamePlayerId(room, playerId));
        }, playerId => new GameActionPayload("CardsDrawn", playerId, null, drawCount));
    }

    public Task ChallengeWildDrawFour(string code, string accessToken)
    {
        Guid? drawTargetPlayerId = null;
        var drawCount = 0;
        return ExecuteAction(code, accessToken, (room, playerId) =>
        {
            var game = RequireGame(room);
            var result = new UnoGameEngine().ChallengeWildDrawFour(game, GetGamePlayerId(room, playerId));
            drawTargetPlayerId = room.Players.First(player => player.GamePlayerId == result.DrawnPlayerId).Id;
            drawCount = result.DrawCount;
        }, _ => drawTargetPlayerId is null
            ? null
            : new GameActionPayload("CardsDrawn", drawTargetPlayerId.Value, null, drawCount));
    }

    public async Task CallUno(string code, string accessToken)
    {
        var player = roomStore.ExecuteLocked(code, room =>
        {
            var playerId = roomStore.AuthorizeParticipant(code, accessToken);
            var participant = room.Players.First(candidate => candidate.Id == playerId);
            new UnoGameEngine().CallUno(RequireGame(room), GetGamePlayerId(room, playerId));
            return participant.DisplayName;
        });

        await Clients.Group(RoomRealtimePublisher.RoomGroup(code))
            .SendAsync("UnoCalled", player);
        await publisher.PublishRoomAsync(code);
        await publisher.PublishGameStateAsync(code);
    }

    public Task CatchUno(string code, string accessToken, Guid targetPlayerId) =>
        ExecuteAction(code, accessToken, (room, playerId) =>
            new UnoGameEngine().CatchUno(
                RequireGame(room),
                GetGamePlayerId(room, playerId),
                GetGamePlayerId(room, targetPlayerId)),
            _ => new GameActionPayload("CardsDrawn", targetPlayerId, null, 2));

    private async Task ExecuteAction(
        string code,
        string accessToken,
        Action<UnoGameRoom, Guid> action,
        Func<Guid, GameActionPayload?>? actionEventFactory = null)
    {
        var gameAction = roomStore.ExecuteLocked(code, room =>
        {
            var playerId = roomStore.AuthorizeParticipant(code, accessToken);
            action(room, playerId);
            return actionEventFactory?.Invoke(playerId);
        });

        if (gameAction is not null)
            await publisher.PublishGameActionAsync(code, gameAction);

        await publisher.PublishRoomAsync(code);
        await publisher.PublishGameStateAsync(code);
    }

    private static UnoGameState RequireGame(UnoGameRoom room) =>
        room.Game ?? throw new InvalidOperationException("The game has not started.");

    private static Guid GetGamePlayerId(UnoGameRoom room, Guid roomPlayerId)
    {
        var participant = room.Players.FirstOrDefault(player => player.Id == roomPlayerId)
            ?? throw new UnauthorizedAccessException("The player is not a member of this room.");

        return participant.GamePlayerId
            ?? throw new InvalidOperationException("The game has not started for this player.");
    }

}
