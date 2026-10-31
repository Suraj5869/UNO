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
    private readonly Dictionary<string, Dictionary<Guid, string>> _rematchCandidatesByRoomCode =
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

    /// <summary>
    /// Starts or joins the fresh lobby for a completed room. The first player
    /// becomes its host; other previous participants can opt in while the
    /// rematch lobby is open.
    /// </summary>
    public (UnoGameRoom Room, Guid PlayerId, string AccessToken) PlayAgain(
        string code,
        string accessToken)
    {
        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            if (string.IsNullOrWhiteSpace(accessToken) ||
                !_tokensByRoomCode[room.Code].TryGetValue(accessToken, out var playerId))
            {
                throw new UnauthorizedAccessException("The room access token is invalid.");
            }

            if (room.Status == UnoRoomStatus.InProgress && room.Game?.Status == GameStatus.Completed)
            {
                var candidates = room.Players
                    .Where(player => player.Id != playerId)
                    .ToDictionary(player => player.Id, player => player.DisplayName);
                room.PrepareRematch(playerId);
                _rematchCandidatesByRoomCode[room.Code] = candidates;
            }
            else if (room.Status == UnoRoomStatus.Waiting)
            {
                if (room.Players.Any(player => player.Id == playerId))
                {
                    return (room, playerId, accessToken);
                }

                if (!_rematchCandidatesByRoomCode.TryGetValue(room.Code, out var candidates) ||
                    !candidates.TryGetValue(playerId, out var displayName))
                {
                    throw new InvalidOperationException("This player is no longer eligible to join the rematch lobby.");
                }

                room.JoinRematch(playerId, displayName);
                candidates.Remove(playerId);
            }
            else
            {
                throw new InvalidOperationException("Play Again is available only after a match has completed.");
            }

            return (room, playerId, accessToken);
        }
    }

    /// <summary>Starts a lobby game and closes the previous-rematch opt-in window.</summary>
    public UnoGameState StartGame(string code, string accessToken)
    {
        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            if (string.IsNullOrWhiteSpace(accessToken) ||
                !_tokensByRoomCode[room.Code].TryGetValue(accessToken, out var playerId))
            {
                throw new UnauthorizedAccessException("The room access token is invalid.");
            }

            var game = room.StartGame(playerId);
            if (_rematchCandidatesByRoomCode.Remove(room.Code, out var candidates))
            {
                var revokedTokens = _tokensByRoomCode[room.Code]
                    .Where(entry => candidates.ContainsKey(entry.Value))
                    .Select(entry => entry.Key)
                    .ToArray();
                foreach (var revokedToken in revokedTokens)
                {
                    _tokensByRoomCode[room.Code].Remove(revokedToken);
                }
            }
            return game;
        }
    }

    public RoomResponse Leave(string code, string accessToken)
    {
        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            if (string.IsNullOrWhiteSpace(accessToken) ||
                !_tokensByRoomCode[room.Code].TryGetValue(accessToken, out var playerId))
                throw new UnauthorizedAccessException("The room access token is invalid.");

            room.Leave(playerId);
            _tokensByRoomCode[room.Code].Remove(accessToken);
            if (_rematchCandidatesByRoomCode.TryGetValue(room.Code, out var candidates))
                candidates.Remove(playerId);

            return RoomResponse.From(room);
        }
    }

    /// <summary>
    /// Runs a room operation while holding the registry lock. The first
    /// multiplayer implementation uses this to serialize game mutations.
    /// </summary>
    public T ExecuteLocked<T>(string code, Func<UnoGameRoom, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_sync)
        {
            return operation(GetByCodeUnsafe(code));
        }
    }

    public IReadOnlyList<string> GetActiveRoomCodes()
    {
        lock (_sync)
        {
            return _roomsByCode.Values.Where(room => room.Status == UnoRoomStatus.InProgress && room.Game is not null)
                .Select(room => room.Code).ToArray();
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
            if (_tokensByRoomCode[room.Code].TryGetValue(accessToken, out var playerId) &&
                room.Players.Any(player => player.Id == playerId))
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
