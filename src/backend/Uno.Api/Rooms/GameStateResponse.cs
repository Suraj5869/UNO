using Uno.GameEngine;

namespace Uno.Api.Rooms;

/// <summary>
/// Client-safe game state. Only the requesting player's hand is included;
/// opponents receive their card counts but never their card contents.
/// </summary>
public sealed record GameStateResponse(
    string RoomCode,
    UnoRoomStatus RoomStatus,
    GameStatus GameStatus,
    UnoRuleSet RuleSet,
    Guid ViewerPlayerId,
    Guid CurrentPlayerId,
    CardColor ActiveColor,
    UnoCardResponse TopDiscard,
    int DrawPileCount,
    int DiscardPileCount,
    int TurnDirection,
    int PendingDrawAmount,
    CardType? PendingDrawType,
    bool IsWildDrawFourChallengePending,
    bool CanPassAfterDraw,
    Guid? PlayerAwaitingUnoCallId,
    IReadOnlyList<GamePlayerResponse> Players)
{
    public static GameStateResponse From(UnoGameRoom room, Guid viewerPlayerId)
    {
        var game = room.Game
            ?? throw new InvalidOperationException("The room has not started a game.");

        var viewer = room.Players.FirstOrDefault(player => player.Id == viewerPlayerId)
            ?? throw new UnauthorizedAccessException("The player is not a member of this room.");

        var currentRoomPlayer = room.Players.FirstOrDefault(
            player => player.GamePlayerId == game.CurrentPlayer.Id);

        var awaitingUnoRoomPlayerId = room.Players.FirstOrDefault(
            player => player.GamePlayerId == game.PlayerAwaitingUnoCallId)?.Id;

        return new GameStateResponse(
            room.Code,
            room.Status,
            game.Status,
            game.RuleSet,
            viewer.Id,
            currentRoomPlayer?.Id ?? Guid.Empty,
            game.ActiveColor,
            UnoCardResponse.From(game.TopDiscard),
            game.DrawPile.Count,
            game.DiscardPile.Count,
            game.TurnDirection,
            game.PendingDrawAmount,
            game.PendingDrawType,
            game.IsWildDrawFourChallengePending,
            game.CanPassAfterDraw,
            awaitingUnoRoomPlayerId,
            room.Players.Select(roomPlayer =>
            {
                var gamePlayer = game.Players.First(player => player.Id == roomPlayer.GamePlayerId);
                var isViewer = roomPlayer.Id == viewer.Id;
                return new GamePlayerResponse(
                    roomPlayer.Id,
                    roomPlayer.DisplayName,
                    gamePlayer.Hand.Count,
                    isViewer
                        ? gamePlayer.Hand.Select(UnoCardResponse.From).ToArray()
                        : null);
            }).ToArray());
    }
}

public sealed record GamePlayerResponse(
    Guid PlayerId,
    string DisplayName,
    int CardCount,
    IReadOnlyList<UnoCardResponse>? Hand);

public sealed record UnoCardResponse(CardColor Color, CardType Type, int? Number)
{
    public static UnoCardResponse From(UnoCard card) =>
        new(card.Color, card.Type, card.Number);
}
