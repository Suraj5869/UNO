using System.Security.Cryptography;
using Uno.GameEngine;

namespace Uno.Api.Rooms;

/// <summary>
/// Process-local room registry for the first API milestone. Replace with a
/// durable distributed store before deploying multiple backend instances.
/// Room access tokens provide basic possession-based authorization until
/// account authentication is added.
/// </summary>
public sealed class RoomStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, UnoGameRoom> _roomsByCode =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Guid>> _tokensByRoomCode =
        new(StringComparer.OrdinalIgnoreCase);

    public (UnoGameRoom Room, Guid PlayerId, string AccessToken) Create(
        CreateRoomRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var room = new UnoGameRoom(
            request.CreatorDisplayName,
            request.MaximumPlayers,
            request.RuleSet);
        var accessToken = GenerateAccessToken();

        lock (_sync)
        {
            if (_roomsByCode.ContainsKey(room.Code))
            {
                throw new InvalidOperationException(
                    "Room code collision. Please retry room creation.");
            }

            _roomsByCode.Add(room.Code, room);
            _tokensByRoomCode.Add(
                room.Code,
                new Dictionary<string, Guid>(StringComparer.Ordinal)
                {
                    [accessToken] = room.CreatorPlayerId
                });
        }

        return (room, room.CreatorPlayerId, accessToken);
    }

    public (UnoGameRoom Room, Guid PlayerId, string AccessToken) Join(
        string code,
        string displayName)
    {
        var accessToken = GenerateAccessToken();

        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            var player = room.Join(displayName);
            _tokensByRoomCode[room.Code].Add(accessToken, player.Id);
            return (room, player.Id, accessToken);
        }
    }

    public UnoGameRoom GetByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Room code is required.", nameof(code));
        }

        lock (_sync)
        {
            return GetByCodeUnsafe(code);
        }
    }

    public Guid AuthorizeParticipant(string code, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new UnauthorizedAccessException("A room access token is required.");
        }

        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            if (_tokensByRoomCode[room.Code].TryGetValue(accessToken, out var playerId))
            {
                return playerId;
            }
        }

        throw new UnauthorizedAccessException("The room access token is invalid.");
    }

    private UnoGameRoom GetByCodeUnsafe(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Room code is required.", nameof(code));
        }

        if (_roomsByCode.TryGetValue(code.Trim(), out var room))
        {
            return room;
        }

        throw new KeyNotFoundException("Room was not found.");
    }

    private static string GenerateAccessToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
