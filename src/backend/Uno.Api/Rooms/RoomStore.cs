using System.Security.Cryptography;
using Uno.GameEngine;
using Uno.Api.Progression;

namespace Uno.Api.Rooms;

/// <summary>
/// Process-local room registry for the first API milestone. Replace with a
/// durable distributed store before deploying multiple backend instances.
/// Room access tokens provide basic possession-based authorization until
/// account authentication is added.
/// </summary>
public sealed class RoomStore(Func<UnoGameRoom, Random?>? dealRandom = null)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, UnoGameRoom> _roomsByCode =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Guid>> _tokensByRoomCode =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<Guid, string>> _rematchCandidatesByRoomCode =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<Guid, BotDifficulty> _bots = new();
    private readonly Dictionary<Guid,int> _practiceMatches = new();
    private readonly HashSet<Guid> _rankedMatches = new();
    public BotDifficulty? GetBot(Guid seatId) { lock (_sync) return _bots.TryGetValue(seatId,out var difficulty) ? difficulty : null; }
    public void AddBots(string code, string token, int count, BotDifficulty difficulty)
    {
        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            if (_matchmakingRooms.Contains(room.Code)) throw new InvalidOperationException("Bots are not allowed in public queues.");
            if (AuthorizeParticipant(code,token)!=room.CreatorPlayerId) throw new UnauthorizedAccessException("Only the host can add bots.");
            if (!Enum.IsDefined(difficulty) || count<1 || count>room.MaximumPlayers-room.Players.Count || room.Status!=UnoRoomStatus.Waiting)
                throw new ArgumentException("Invalid bot count, difficulty or room status.");
            for(var i=0;i<count;i++)
            {
                var seat=room.Join($"Bot {room.Players.Count} · {difficulty}");
                _bots.Add(seat.Id,difficulty);
            }
        }
    }
    public (UnoGameRoom Room, Guid PlayerId, string AccessToken) CreatePractice(string name,int count,BotDifficulty difficulty,UnoRuleSet rules,Guid? userId)
    {
        if(count is <1 or >9 || !Enum.IsDefined(difficulty)) throw new ArgumentException("Choose 1–9 bots and a valid difficulty.");
        lock(_sync)
        {
            var access=Create(new CreateRoomRequest(name,count+1,rules),userId);
            AddBots(access.Room.Code,access.AccessToken,count,difficulty);
            StartGame(access.Room.Code,access.AccessToken);
            return access;
        }
    }
    private readonly Dictionary<Guid, Guid> _accounts = new();
    private readonly Dictionary<Guid, CompletedMatch> _pendingResults = new();
    public CompletedMatch[] PendingResults() { lock (_sync) return _pendingResults.Values.ToArray(); }
    public void AcknowledgeResult(Guid id) { lock (_sync) _pendingResults.Remove(id); }

    private void CaptureResult(UnoGameRoom room, bool abandoned = false)
    {
        if (room.Game is not {} game || (!abandoned && game.Status != GameStatus.Completed) || _captured.Contains(game.MatchId)) return;
        var winner = game.Players.FirstOrDefault(p => p.Hand.Count == 0);
        if (!abandoned && (winner is null || game.Players.Count < 2)) return;
        var participants = room.Players.Select(p => new ResultPlayer(p.Id,
            _accounts.TryGetValue(p.Id, out var account) ? account : null,
            p.DisplayName, !abandoned && p.GamePlayerId == winner?.Id)).ToArray();
        if (!participants.Any(p => p.UserId.HasValue)) return;
        _captured.Add(game.MatchId);
        _pendingResults.TryAdd(game.MatchId, new CompletedMatch(game.MatchId, room.Id, room.Code,
            room.MaximumPlayers, (int)game.RuleSet, game.StartedAt, DateTimeOffset.UtcNow, game.CardsPlayed, participants, _rankedMatches.Contains(game.MatchId) ? "ranked" : _practiceMatches.ContainsKey(game.MatchId) ? "bots" : "rooms", _practiceMatches.TryGetValue(game.MatchId,out var difficulty) ? difficulty : null, abandoned));
    }

    private readonly HashSet<Guid> _captured = new();
    private readonly HashSet<string> _matchmakingRooms = new(StringComparer.OrdinalIgnoreCase);

    // Waiting public rooms are FIFO queues for an exact player count and rule set.
    public (UnoGameRoom Room, Guid PlayerId, string AccessToken) FindMatch(string displayName, Guid? userId = null, int maximumPlayers = 2, UnoRuleSet ruleSet = UnoRuleSet.Official)
    {
        if (maximumPlayers is < 2 or > 10 || !Enum.IsDefined(ruleSet))
            throw new ArgumentException("Choose 2–10 players and a valid rule set.");
        lock (_sync)
        {
            if (userId.HasValue && _roomsByCode.Values.Any(room =>
                _matchmakingRooms.Contains(room.Code) && room.Players.Any(p => _accounts.GetValueOrDefault(p.Id) == userId.Value)))
                throw new InvalidOperationException("Your account is already searching for a match.");
            var waiting = _roomsByCode.Values.FirstOrDefault(room =>
                _matchmakingRooms.Contains(room.Code) && room.Status == UnoRoomStatus.Waiting &&
                room.MaximumPlayers == maximumPlayers && room.RuleSet == ruleSet &&
                room.Players.Count < maximumPlayers);
            if (waiting is null)
            {
                var created = Create(new CreateRoomRequest(displayName, maximumPlayers, ruleSet), userId);
                _matchmakingRooms.Add(created.Room.Code);
                return created;
            }
            var joined = Join(waiting.Code, displayName, userId, publicQueue: true);
            if (waiting.Players.Count == maximumPlayers)
            {
                var ranked = waiting.StartGame(waiting.CreatorPlayerId, dealRandom?.Invoke(waiting));
                _rankedMatches.Add(ranked.MatchId);
                _matchmakingRooms.Remove(waiting.Code);
            }
            return joined;
        }
    }

    public RoomResponse CancelSearch(string code,string token)
    {
        lock (_sync)
        {
            if (!_matchmakingRooms.Contains(code)) throw new InvalidOperationException("The match has already started or the search has ended.");
            return Leave(code,token);
        }
    }

    public bool CanInvite(string code,string token,Guid accountId)
    {
        lock (_sync)
        {
            var room=GetByCodeUnsafe(code);
            var seat=AuthorizeParticipant(code,token);
            return _accounts.GetValueOrDefault(seat)==accountId && room.Status==UnoRoomStatus.Waiting
                && !_matchmakingRooms.Contains(room.Code) && room.Players.Count<room.MaximumPlayers;
        }
    }

    public bool IsSearching(string code) { lock (_sync) return _matchmakingRooms.Contains(code); }

    public (UnoGameRoom Room, Guid PlayerId, string AccessToken) Create(
        CreateRoomRequest request, Guid? userId = null)
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

            if (userId.HasValue) _accounts.Add(room.CreatorPlayerId, userId.Value);
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
        string displayName, Guid? userId = null, bool publicQueue = false)
    {
        var accessToken = GenerateAccessToken();

        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            if (_matchmakingRooms.Contains(room.Code) && !publicQueue) throw new InvalidOperationException("Join public matches through matchmaking.");
            if (userId.HasValue && room.Players.Any(p => _accounts.GetValueOrDefault(p.Id) == userId.Value))
                throw new InvalidOperationException("Account already occupies a seat.");
            var player = room.Join(displayName);
            if (userId.HasValue) _accounts.Add(player.Id, userId.Value);
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
                CaptureResult(room);
                room.PrepareRematch(playerId);
                foreach(var bot in candidates.Where(p=>_bots.ContainsKey(p.Key)).ToArray()) { room.JoinRematch(bot.Key,bot.Value); candidates.Remove(bot.Key); }
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

            if (_matchmakingRooms.Contains(room.Code)) throw new InvalidOperationException("Public matches start automatically when full.");
            var game = room.StartGame(playerId, dealRandom?.Invoke(room));
            if(room.Players.Count(p=>!_bots.ContainsKey(p.Id))==1 && room.Players.Any(p=>_bots.ContainsKey(p.Id))) _practiceMatches[game.MatchId]=room.Players.Where(p=>_bots.ContainsKey(p.Id)).Min(p=>(int)_bots[p.Id]);
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

            CaptureResult(room);
            if (room.Game is { Status: GameStatus.InProgress }) CaptureResult(room, abandoned:true);
            room.Leave(playerId);
            _accounts.Remove(playerId);
            _tokensByRoomCode[room.Code].Remove(accessToken);
            if(room.Players.All(p=>_bots.ContainsKey(p.Id))) { foreach(var bot in room.Players) _bots.Remove(bot.Id); _matchmakingRooms.Remove(room.Code); }
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
            var room = GetByCodeUnsafe(code);
            var result = operation(room);
            CaptureResult(room);
            return result;
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
