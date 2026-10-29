using Uno.GameEngine;

namespace Uno.Api.Rooms;

public sealed record CreateRoomRequest(
    string CreatorDisplayName,
    int MaximumPlayers = UnoGameFactory.MaximumPlayers,
    UnoRuleSet RuleSet = UnoRuleSet.Official);

public sealed record JoinRoomRequest(string DisplayName);

public sealed record ChangeRoomRulesRequest(string AccessToken, UnoRuleSet RuleSet);

public sealed record StartRoomRequest(string AccessToken);

public sealed record RoomPlayerResponse(
    Guid Id,
    string DisplayName,
    bool IsCreator,
    Guid? GamePlayerId);

public sealed record RoomResponse(
    Guid Id,
    string Code,
    int MaximumPlayers,
    UnoRuleSet RuleSet,
    UnoRoomStatus Status,
    IReadOnlyList<RoomPlayerResponse> Players,
    GameStatus? GameStatus)
{
    public static RoomResponse From(UnoGameRoom room) =>
        new(
            room.Id,
            room.Code,
            room.MaximumPlayers,
            room.RuleSet,
            room.Status,
            room.Players.Select(player => new RoomPlayerResponse(
                player.Id,
                player.DisplayName,
                player.IsCreator,
                player.GamePlayerId)).ToArray(),
            room.Game?.Status);
}

/// <summary>
/// AccessToken is a room-specific secret. Return it only to the participant
/// who joined; clients must not share or log it.
/// </summary>
public sealed record RoomAccessResponse(
    RoomResponse Room,
    Guid PlayerId,
    string AccessToken);
