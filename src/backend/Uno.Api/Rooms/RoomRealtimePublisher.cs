using Microsoft.AspNetCore.SignalR;
using Uno.Api.Hubs;

namespace Uno.Api.Rooms;

/// <summary>
/// Sends lobby summaries and player-specific game state to connected clients.
/// </summary>
public sealed class RoomRealtimePublisher(
    RoomStore roomStore,
    IHubContext<UnoRoomHub> hubContext)
{
    public static string RoomGroup(string code) => $"uno-room:{code.Trim().ToUpperInvariant()}";

    public static string PlayerGroup(string code, Guid playerId) =>
        $"{RoomGroup(code)}:player:{playerId:N}";

    public async Task PublishRoomAsync(string code)
    {
        var room = roomStore.ExecuteLocked(code, room => RoomResponse.From(room));
        await hubContext.Clients.Group(RoomGroup(code))
            .SendAsync("RoomUpdated", room);
    }

    public async Task PublishGameStateAsync(string code)
    {
        var states = roomStore.ExecuteLocked(code, room =>
        {
            if (room.Game is null)
            {
                return Array.Empty<(Guid PlayerId, GameStateResponse State)>();
            }

            return room.Players
                .Select(player => (
                    PlayerId: player.Id,
                    State: GameStateResponse.From(room, player.Id)))
                .ToArray();
        });

        foreach (var (playerId, state) in states)
        {
            await hubContext.Clients.Group(PlayerGroup(code, playerId))
                .SendAsync("GameStateUpdated", state);
        }
    }
}
