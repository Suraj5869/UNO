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
    private void RequireSession(string code, string token)
    {
        var principal = Context.User;
        Guid? account = Guid.TryParse(principal?.FindFirst("sub")?.Value, out var user) ? user : null;
        Guid? session = Guid.TryParse(principal?.FindFirst("sid")?.Value, out var sid) ? sid : null;
        roomStore.RequireSession(code, token, account, session);
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        roomStore.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task<RoomResponse> JoinRoom(string code, string accessToken)
    {
        RequireSession(code, accessToken);
        var playerId = roomStore.AuthorizeParticipant(code, accessToken);
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomRealtimePublisher.PlayerGroup(code, playerId));
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomRealtimePublisher.RoomGroup(code));

        // Capture after subscription so an update cannot fall between the
        // initial snapshot and group membership.
        try
        {
            var joined = roomStore.ExecuteLocked(code, room =>
            {
                roomStore.Connected(code, accessToken, Context.ConnectionId);
                return (Room: RoomResponse.From(room), GameState: room.Game is null
                    ? null : GameStateResponse.From(room, playerId));
            });
            await Clients.Caller.SendAsync("RoomUpdated", joined.Room);
            if (joined.GameState is not null)
                await Clients.Caller.SendAsync("GameStateUpdated", joined.GameState);
            return joined.Room;
        }
        catch
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomRealtimePublisher.PlayerGroup(code, playerId));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomRealtimePublisher.RoomGroup(code));
            throw;
        }
    }

    public async Task LeaveRoom(string code, string accessToken)
    {
        RequireConnection(code, accessToken);
        RequireSession(code, accessToken);
        var playerId = roomStore.AuthorizeParticipant(code, accessToken);
        roomStore.Leave(code, accessToken);

        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            RoomRealtimePublisher.PlayerGroup(code, playerId));
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            RoomRealtimePublisher.RoomGroup(code));

        await publisher.PublishRoomAsync(code);
        await publisher.PublishGameStateAsync(code);
    }

    public Task<GameStateResponse> GetGameState(string code, string accessToken)
    {
        RequireConnection(code, accessToken);
        return Task.FromResult(roomStore.ExecuteLocked(code, room =>
        {
            RequireSession(code, accessToken);
        var playerId = roomStore.AuthorizeParticipant(code, accessToken);
            return GameStateResponse.From(room, playerId);
        }));
    }

    public Task PlayCard(
        string code,
        string accessToken,
        string revision,
        int cardIndex,
        CardColor chosenColor)
    {
        UnoCard? playedCard = null;
        Guid? automaticDrawTargetPlayerId = null;
        var automaticDrawCount = 0;
        UnoPlayer? drawTarget = null;
        var beforeDraw = 0;
        return ExecuteAction(code, accessToken, revision, (room, playerId) =>
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
                drawTarget = game.Players[nextPlayerIndex];
                beforeDraw = drawTarget.Hand.Count;
            }

            new UnoGameEngine().PlayCard(game, gamePlayerId, card, chosenColor);
            if (drawTarget is not null) automaticDrawCount = drawTarget.Hand.Count - beforeDraw;
        }, playerId => playedCard is null
            ? null
            : new GameActionPayload("CardPlayed", playerId, UnoCardResponse.From(playedCard), automaticDrawCount, automaticDrawTargetPlayerId));
    }

    public Task DrawCard(string code, string accessToken, string revision) =>
        ExecuteAction(code, accessToken, revision, (room, playerId) =>
            new UnoGameEngine().DrawCard(RequireGame(room), GetGamePlayerId(room, playerId)),
            playerId => new GameActionPayload("CardDrawn", playerId, null, 1));

    public Task PassAfterDraw(string code, string accessToken, string revision) =>
        ExecuteAction(code, accessToken, revision, (room, playerId) =>
            new UnoGameEngine().PassAfterDraw(RequireGame(room), GetGamePlayerId(room, playerId)));

    public Task AcceptDrawPenalty(string code, string accessToken, string revision)
    {
        var drawCount = 0;
        return ExecuteAction(code, accessToken, revision, (room, playerId) =>
        {
            var game = RequireGame(room);
            var before = game.CurrentPlayer;
            var beforeCount = before.Hand.Count;
            new UnoGameEngine().AcceptDrawPenalty(game, GetGamePlayerId(room, playerId));
            drawCount = before.Hand.Count - beforeCount;
        }, playerId => drawCount > 0 ? new GameActionPayload("CardsDrawn", playerId, null, drawCount) : null);
    }

    public Task AcceptWildDrawFour(string code, string accessToken, string revision)
    {
        var drawCount = 0;
        return ExecuteAction(code, accessToken, revision, (room, playerId) =>
        {
            var game = RequireGame(room);
            var before = game.CurrentPlayer;
            var beforeCount = before.Hand.Count;
            new UnoGameEngine().AcceptWildDrawFour(game, GetGamePlayerId(room, playerId));
            drawCount = before.Hand.Count - beforeCount;
        }, playerId => drawCount > 0 ? new GameActionPayload("CardsDrawn", playerId, null, drawCount) : null);
    }

    public Task ChallengeWildDrawFour(string code, string accessToken, string revision)
    {
        Guid? drawTargetPlayerId = null;
        var drawCount = 0;
        return ExecuteAction(code, accessToken, revision, (room, playerId) =>
        {
            var game = RequireGame(room);
            var result = new UnoGameEngine().ChallengeWildDrawFour(game, GetGamePlayerId(room, playerId));
            drawTargetPlayerId = room.Players.First(player => player.GamePlayerId == result.DrawnPlayerId).Id;
            drawCount = result.DrawCount;
        }, _ => drawTargetPlayerId is null || drawCount == 0
            ? null
            : new GameActionPayload("CardsDrawn", drawTargetPlayerId.Value, null, drawCount));
    }

    public async Task CallUno(string code, string accessToken, string revision)
    {
        var player = roomStore.ExecuteLocked(code, room =>
        {
            RequireSession(code, accessToken);
        var playerId = roomStore.AuthorizeParticipant(code, accessToken);
            RequireConnection(code, accessToken);
            RequireRevision(room, revision);
            var participant = room.Players.First(candidate => candidate.Id == playerId);
            new UnoGameEngine().CallUno(RequireGame(room), GetGamePlayerId(room, playerId));
            return participant.DisplayName;
        });

        await Clients.Clients(roomStore.AuthorizedConnections(code))
            .SendAsync("UnoCalled", player);
        await publisher.PublishRoomAsync(code);
        await publisher.PublishGameStateAsync(code);
    }

    public Task CatchUno(string code, string accessToken, string revision, Guid targetPlayerId)
    {
        var drawCount = 0;
        return ExecuteAction(code, accessToken, revision, (room, playerId) =>
        {
            var game = RequireGame(room);
            var targetId = GetGamePlayerId(room, targetPlayerId);
            var target = game.Players.First(player => player.Id == targetId);
            var before = target.Hand.Count;
            new UnoGameEngine().CatchUno(game, GetGamePlayerId(room, playerId), targetId);
            drawCount = target.Hand.Count - before;
        }, _ => drawCount > 0 ? new GameActionPayload("CardsDrawn", targetPlayerId, null, drawCount) : null);
    }

    private async Task ExecuteAction(
        string code,
        string accessToken,
        string revision,
        Action<UnoGameRoom, Guid> action,
        Func<Guid, GameActionPayload?>? actionEventFactory = null)
    {
        var gameAction = roomStore.ExecuteLocked(code, room =>
        {
            RequireSession(code, accessToken);
        var playerId = roomStore.AuthorizeParticipant(code, accessToken);
            RequireConnection(code, accessToken);
            RequireRevision(room, revision);
            var wasCurrent = room.Game is {} before && before.CurrentPlayer.Id == GetGamePlayerId(room,playerId);
            action(room, playerId);
            if (wasCurrent && room.Game is {} game) roomStore.MarkParticipation(game.MatchId,playerId);
            var payload = actionEventFactory?.Invoke(playerId);
            return payload is null ? null : publisher.CaptureGameAction(code, payload);
        });

        if (gameAction is not null)
            await publisher.PublishGameActionAsync(code, gameAction);

        await publisher.PublishRoomAsync(code);
        await publisher.PublishGameStateAsync(code);
    }

    private static void RequireRevision(UnoGameRoom room, string revision)
    {
        try { GameStateResponse.RequireRevision(room, revision); }
        catch (InvalidOperationException) { throw new HubException("Game state changed. Refresh and retry."); }
    }

    private void RequireConnection(string code, string accessToken)
    {
        RequireSession(code, accessToken);
        var playerId = roomStore.AuthorizeParticipant(code, accessToken);
        if (!roomStore.AuthorizedConnections(code, playerId).Contains(Context.ConnectionId))
            throw new HubException("Join the room on this connection before performing actions.");
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
