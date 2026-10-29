using Uno.GameEngine;

namespace Uno.Api.Rooms;

/// <summary>
/// Process-local room registry for the first API milestone. Replace with a
/// durable distributed store before deploying multiple backend instances.
/// </summary>
public sealed class RoomStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, UnoGameRoom> _roomsByCode =
        new(StringComparer.OrdinalIgnoreCase);

    public UnoGameRoom Create(CreateRoomRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var room = new UnoGameRoom(
            request.CreatorDisplayName,
            request.MaximumPlayers,
            request.RuleSet);

        lock (_sync)
        {
            if (_roomsByCode.ContainsKey(room.Code))
            {
                throw new InvalidOperationException(
                    "Room code collision. Please retry room creation.");
            }

            _roomsByCode.Add(room.Code, room);
        }

        return room;
    }

    public UnoGameRoom GetByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Room code is required.", nameof(code));
        }

        lock (_sync)
        {
            if (_roomsByCode.TryGetValue(code.Trim(), out var room))
            {
                return room;
            }
        }

        throw new KeyNotFoundException("Room was not found.");
    }
}
