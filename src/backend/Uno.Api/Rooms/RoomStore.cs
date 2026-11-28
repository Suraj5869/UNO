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
public sealed class RoomStore(Func<UnoGameRoom, Random?>? dealRandom = null, TimeProvider? clock = null)
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, DateTimeOffset> _credentialExpiry = new();
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();
    private bool CredentialActive(Guid seat) => _credentialExpiry.TryGetValue(seat, out var expiry) && expiry > Now;
    private void IssueCredential(Guid seat) => _credentialExpiry[seat] = Now.AddHours(24);
    private readonly Dictionary<string, UnoGameRoom> _roomsByCode =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Guid>> _tokensByRoomCode =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<Guid, string>> _rematchCandidatesByRoomCode =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<Guid, BotDifficulty> _bots = new();
    private readonly Dictionary<Guid,int> _practiceMatches = new();
    private readonly HashSet<Guid> _rankedMatches = new();
    private readonly Dictionary<Guid,List<ResultPlayer>> _departed = new();
    private readonly Dictionary<Guid,HashSet<Guid>> _participation = new();
    private readonly Dictionary<Guid,(string Code,string Token,string? Connection,DateTimeOffset? Expires)> _presence = new();

    public sealed record RuntimeCheckpoint(UnoGameRoom.Checkpoint[] Rooms,Dictionary<string,Dictionary<string,Guid>> Tokens,Dictionary<string,Dictionary<Guid,string>> Rematches,Dictionary<Guid,BotDifficulty> Bots,Dictionary<Guid,int> Practice,Guid[] Ranked,Dictionary<Guid,List<ResultPlayer>> Departed,Dictionary<Guid,HashSet<Guid>> Participation,Dictionary<Guid,Guid> Accounts,CompletedMatch[] Pending,Guid[] Captured,string[] Queues, Dictionary<Guid,Guid>? SeatSessions = null, Dictionary<Guid,DateTimeOffset>? CredentialExpiry = null);
    public string ExportRuntime()
    {
        lock (_sync) return System.Text.Json.JsonSerializer.Serialize(new RuntimeCheckpoint(_roomsByCode.Values.Select(r=>r.ExportCheckpoint()).ToArray(),_tokensByRoomCode,_rematchCandidatesByRoomCode,_bots,_practiceMatches,_rankedMatches.ToArray(),_departed,_participation,_accounts,_pendingResults.Values.ToArray(),_captured.ToArray(),_matchmakingRooms.ToArray(),_seatSessions,_credentialExpiry));
    }
    public void RestoreRuntime(string json)
    {
        var c=System.Text.Json.JsonSerializer.Deserialize<RuntimeCheckpoint>(json) ?? throw new InvalidOperationException("Invalid runtime checkpoint");
        lock (_sync)
        {
            foreach(var room in c.Rooms) _roomsByCode.Add(room.Code,UnoGameRoom.RestoreCheckpoint(room));
            foreach(var p in c.Tokens) { _tokensByRoomCode.Add(p.Key,p.Value); foreach(var token in p.Value) { _presence[token.Value]=(p.Key,token.Key,null,Now.AddSeconds(60)); _credentialExpiry[token.Value]=c.CredentialExpiry?.GetValueOrDefault(token.Value) ?? Now; } }
            foreach(var p in c.Rematches) _rematchCandidatesByRoomCode.Add(p.Key,p.Value);
            foreach(var p in c.Bots) _bots.Add(p.Key,p.Value);
            foreach(var p in c.Practice) _practiceMatches.Add(p.Key,p.Value);
            _rankedMatches.UnionWith(c.Ranked);
            foreach(var p in c.Departed) _departed.Add(p.Key,p.Value);
            foreach(var p in c.Participation) _participation.Add(p.Key,p.Value);
            foreach(var p in c.Accounts) _accounts.Add(p.Key,p.Value);
            if (c.SeatSessions is not null) foreach(var p in c.SeatSessions) _seatSessions.Add(p.Key,p.Value);
            foreach(var p in c.Pending) _pendingResults.Add(p.Id,p);
            _captured.UnionWith(c.Captured);
            _matchmakingRooms.UnionWith(c.Queues);
        }
    }
    public void CleanupEmptyRooms()
    {
        lock (_sync)
        foreach(var room in _roomsByCode.Values.Where(r=>r.Players.All(p=>_bots.ContainsKey(p.Id)) || _tokensByRoomCode[r.Code].Count==0).ToArray())
        {
            foreach(var seat in room.Players) { _bots.Remove(seat.Id); _accounts.Remove(seat.Id); _seatSessions.Remove(seat.Id); _presence.Remove(seat.Id); _credentialExpiry.Remove(seat.Id); }
            foreach(var seat in _tokensByRoomCode[room.Code].Values) { _accounts.Remove(seat); _seatSessions.Remove(seat); _presence.Remove(seat); _credentialExpiry.Remove(seat); }
            if(room.Game is {} game) { _captured.Remove(game.MatchId); _rankedMatches.Remove(game.MatchId); _practiceMatches.Remove(game.MatchId); _departed.Remove(game.MatchId); _participation.Remove(game.MatchId); }
            _roomsByCode.Remove(room.Code); _tokensByRoomCode.Remove(room.Code); _rematchCandidatesByRoomCode.Remove(room.Code); _matchmakingRooms.Remove(room.Code);
        }
    }

    public void MarkParticipation(Guid matchId, Guid seat)
    {
        lock (_sync) { if (!_participation.TryGetValue(matchId,out var seats)) _participation[matchId]=seats=new(); seats.Add(seat); }
    }
    // Room seats are bound to the account session that created them.
    private readonly Dictionary<Guid, Guid> _seatSessions = new();
    public void BindSession(Guid seat, Guid? sessionId)
    {
        lock (_sync)
        {
            if (_accounts.ContainsKey(seat))
                _seatSessions[seat] = sessionId ?? throw new UnauthorizedAccessException("Account session required.");
        }
    }
    public void RequireSession(string code, string token, Guid? accountId, Guid? sessionId)
    {
        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            if (!_tokensByRoomCode[room.Code].TryGetValue(token, out var seat) || !CredentialActive(seat)) throw new UnauthorizedAccessException("Invalid room credential.");
            if (_accounts.TryGetValue(seat, out var owner) && (owner != accountId || !sessionId.HasValue || !_seatSessions.TryGetValue(seat, out var bound) || bound != sessionId.Value))
                throw new UnauthorizedAccessException("Room session mismatch.");
        }
    }
    public void RevokeSessionRooms(Guid accountId, Guid sessionId)
    {
        lock (_sync)
        {
            foreach (var seat in _accounts.Where(p => p.Value == accountId && _seatSessions.GetValueOrDefault(p.Key) == sessionId).Select(p => p.Key).ToArray())
            {
                if (_presence.TryGetValue(seat, out var presence))
                {
                    Leave(presence.Code, presence.Token);
                }
            }
        }
    }
    public void Connected(string code,string token,string connection)
    {
        lock (_sync) { var seat=AuthorizeParticipant(code,token); _presence[seat]=(code,token,connection,null); }
    }
    public string[] AuthorizedConnections(string code, Guid? playerId = null)
    {
        lock (_sync)
        {
            if (!_roomsByCode.TryGetValue(code.Trim(), out var room)) return [];
            return room.Players.Where(p => playerId == null || p.Id == playerId)
                .Select(p => _presence.GetValueOrDefault(p.Id))
                .Where(p => p.Connection != null && string.Equals(p.Code, room.Code, StringComparison.OrdinalIgnoreCase)
                    && _tokensByRoomCode[room.Code].TryGetValue(p.Token, out var seat) && CredentialActive(seat))
                .Select(p => p.Connection!).Distinct().ToArray();
        }
    }
    public void Disconnected(string connection)
    {
        lock (_sync) foreach(var entry in _presence.Where(p=>p.Value.Connection==connection).ToArray())
            _presence[entry.Key]=(entry.Value.Code,entry.Value.Token,null,DateTimeOffset.UtcNow.AddSeconds(60));
    }
    public string[] ExpireDisconnected()
    {
        lock (_sync)
        {
            var changed=new HashSet<string>();
            foreach(var entry in _presence.Where(p=>!CredentialActive(p.Key) || (p.Value.Expires is {} expires && expires<=Now)).ToArray())
            {
                _presence.Remove(entry.Key);
                try { Leave(entry.Value.Code,entry.Value.Token); changed.Add(entry.Value.Code); }
                catch (UnauthorizedAccessException) { }
                catch (KeyNotFoundException) { }
            }
            return changed.ToArray();
        }
    }
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
        var winner = game.Players.FirstOrDefault(p => p.Hand.Count == 0) ?? (game.Players.Count == 1 ? game.Players[0] : null);
        if (!abandoned && (winner is null)) return;
        var participants = room.Players.Select(p => new ResultPlayer(p.Id,
            _accounts.TryGetValue(p.Id, out var account) ? account : null,
            p.DisplayName, !abandoned && p.GamePlayerId == winner?.Id, false, _participation.GetValueOrDefault(game.MatchId)?.Contains(p.Id)==true)).Concat(_departed.GetValueOrDefault(game.MatchId) ?? new List<ResultPlayer>()).ToArray();
        _departed.Remove(game.MatchId);
        _participation.Remove(game.MatchId);
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

    public bool CanInvite(string code,string token,Guid accountId, Guid? sessionId = null)
    {
        lock (_sync)
        {
            RequireSession(code, token, accountId, sessionId);
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
            if (_roomsByCode.Count >= 500) throw new InvalidOperationException("Room capacity reached. Try again later.");
            if (userId.HasValue && _accounts.Values.Count(id => id == userId.Value) >= 3) throw new InvalidOperationException("Too many active rooms.");
            if (_roomsByCode.ContainsKey(room.Code))
            {
                throw new InvalidOperationException(
                    "Room code collision. Please retry room creation.");
            }

            if (userId.HasValue) _accounts.Add(room.CreatorPlayerId, userId.Value);
            IssueCredential(room.CreatorPlayerId);
            _presence[room.CreatorPlayerId]=(room.Code,accessToken,null,Now.AddSeconds(60));
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
            if (userId.HasValue && _accounts.Values.Count(id => id == userId.Value) >= 3) throw new InvalidOperationException("Too many active rooms.");
            var player = room.Join(displayName);
            if (userId.HasValue) _accounts.Add(player.Id, userId.Value);
            _tokensByRoomCode[room.Code].Add(accessToken, player.Id);
            IssueCredential(player.Id);
        _presence[player.Id]=(room.Code,accessToken,null,Now.AddSeconds(60));
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
                !_tokensByRoomCode[room.Code].TryGetValue(accessToken, out var playerId) || !CredentialActive(playerId))
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
                if (CredentialActive(playerId) && room.Players.Any(player => player.Id == playerId))
                {
                    return (room, playerId, RotateCredential(room.Code, playerId, accessToken));
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

            return (room, playerId, RotateCredential(room.Code, playerId, accessToken));
        }
    }

    /// <summary>Starts a lobby game and closes the previous-rematch opt-in window.</summary>
    public UnoGameState StartGame(string code, string accessToken)
    {
        lock (_sync)
        {
            var room = GetByCodeUnsafe(code);
            if (string.IsNullOrWhiteSpace(accessToken) ||
                !_tokensByRoomCode[room.Code].TryGetValue(accessToken, out var playerId) || !CredentialActive(playerId))
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
                    var revokedSeat = _tokensByRoomCode[room.Code][revokedToken];
                    _tokensByRoomCode[room.Code].Remove(revokedToken);
                    _accounts.Remove(revokedSeat);
                    _seatSessions.Remove(revokedSeat);
                    _presence.Remove(revokedSeat); _credentialExpiry.Remove(revokedSeat);
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
            if (room.Game is { Status: GameStatus.InProgress } active && room.Players.FirstOrDefault(p=>p.Id==playerId) is {} leaving)
            {
                if (!_departed.TryGetValue(active.MatchId,out var departed)) _departed[active.MatchId]=departed=new();
                departed.Add(new ResultPlayer(playerId,_accounts.TryGetValue(playerId,out var account)?account:null,leaving.DisplayName,false,true));
            }
            room.Leave(playerId);
            if (room.Game is not null) CaptureResult(room, abandoned:room.Players.All(p=>_bots.ContainsKey(p.Id)));
            _accounts.Remove(playerId);
            _seatSessions.Remove(playerId);
            _presence.Remove(playerId); _credentialExpiry.Remove(playerId);
            _tokensByRoomCode[room.Code].Remove(accessToken);
            if(room.Players.All(p=>_bots.ContainsKey(p.Id))) _matchmakingRooms.Remove(room.Code);
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
                CredentialActive(playerId) && room.Players.Any(player => player.Id == playerId))
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

    private string RotateCredential(string code, Guid seat, string oldToken)
    {
        var next = GenerateAccessToken();
        _tokensByRoomCode[code].Remove(oldToken);
        _tokensByRoomCode[code].Add(next, seat);
        IssueCredential(seat);
        _presence[seat] = (code, next, null, Now.AddSeconds(60));
        return next;
    }

    private static string GenerateAccessToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
