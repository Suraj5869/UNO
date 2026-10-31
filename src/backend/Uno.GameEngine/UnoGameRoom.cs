namespace Uno.GameEngine;

/// <summary>
/// In-memory lifecycle for a private Classic UNO room. Persistence, expiry,
/// authentication, and real-time transport are handled by later application
/// layers; this class enforces room rules independent of any client.
/// </summary>
public sealed class UnoGameRoom
{
    private const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly List<UnoRoomPlayer> _players = [];

    public UnoGameRoom(
        string creatorDisplayName,
        int maximumPlayers = UnoGameFactory.MaximumPlayers,
        UnoRuleSet ruleSet = UnoRuleSet.Official)
    {
        if (string.IsNullOrWhiteSpace(creatorDisplayName))
        {
            throw new ArgumentException(
                "Creator display name cannot be empty.",
                nameof(creatorDisplayName));
        }

        if (maximumPlayers < UnoGameFactory.MinimumPlayers ||
            maximumPlayers > UnoGameFactory.MaximumPlayers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPlayers),
                $"Room capacity must be between {UnoGameFactory.MinimumPlayers} and {UnoGameFactory.MaximumPlayers}.");
        }

        if (!Enum.IsDefined(ruleSet))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ruleSet),
                "Select either Official or House rules.");
        }

        Id = Guid.NewGuid();
        Code = GenerateRoomCode();
        MaximumPlayers = maximumPlayers;
        RuleSet = ruleSet;
        Status = UnoRoomStatus.Waiting;
        CreatorPlayerId = AddPlayer(creatorDisplayName, isCreator: true).Id;
    }

    public Guid Id { get; }

    /// <summary>Short code that can be shared to join this room.</summary>
    public string Code { get; }

    public Guid CreatorPlayerId { get; private set; }

    public int MaximumPlayers { get; }

    public UnoRuleSet RuleSet { get; private set; }

    public UnoRoomStatus Status { get; private set; }

    public IReadOnlyList<UnoRoomPlayer> Players => _players.AsReadOnly();

    public UnoGameState? Game { get; private set; }

    public UnoRoomPlayer Join(string displayName)
    {
        EnsureWaiting();

        if (_players.Count >= MaximumPlayers)
        {
            throw new InvalidOperationException("The room is full.");
        }

        return AddPlayer(displayName, isCreator: false);
    }

    /// <summary>Removes a room participant and their active game seat, if any.</summary>
    public bool Leave(Guid playerId)
    {
        var player = _players.FirstOrDefault(candidate => candidate.Id == playerId);
        if (player is null) return false;

        if (Game is { Status: GameStatus.InProgress } game && player.GamePlayerId is { } gamePlayerId)
            new UnoGameEngine().RemovePlayer(game, gamePlayerId);

        _players.Remove(player);

        if (CreatorPlayerId == playerId && _players.Count > 0)
        {
            CreatorPlayerId = _players[0].Id;
            foreach (var participant in _players)
                participant.IsCreator = participant.Id == CreatorPlayerId;
        }

        return true;
    }

    public void SetRuleSet(Guid requestingPlayerId, UnoRuleSet ruleSet)
    {
        EnsureWaiting();
        EnsureCreator(requestingPlayerId);

        if (!Enum.IsDefined(ruleSet))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ruleSet),
                "Select either Official or House rules.");
        }

        RuleSet = ruleSet;
    }

    /// <summary>
    /// Starts a fresh lobby after a completed game, retaining only the player
    /// who opted into the rematch. That player becomes the new lobby host.
    /// </summary>
    public void PrepareRematch(Guid requestingPlayerId)
    {
        if (Game?.Status != GameStatus.Completed || Status != UnoRoomStatus.InProgress)
        {
            throw new InvalidOperationException("A rematch can only be started after the game has completed.");
        }

        var returningPlayer = _players.FirstOrDefault(player => player.Id == requestingPlayerId)
            ?? throw new UnauthorizedAccessException("The player is not a member of this room.");

        _players.Clear();
        returningPlayer.IsCreator = true;
        returningPlayer.GamePlayerId = null;
        _players.Add(returningPlayer);

        CreatorPlayerId = returningPlayer.Id;
        Game = null;
        Status = UnoRoomStatus.Waiting;
    }

    /// <summary>Adds a previous participant who opted into the current rematch lobby.</summary>
    public UnoRoomPlayer JoinRematch(Guid playerId, string displayName)
    {
        EnsureWaiting();

        if (_players.Any(player => player.Id == playerId))
        {
            return _players.First(player => player.Id == playerId);
        }

        if (_players.Count >= MaximumPlayers)
        {
            throw new InvalidOperationException("The room is full.");
        }

        var player = new UnoRoomPlayer(playerId, displayName.Trim(), isCreator: false);
        _players.Add(player);
        return player;
    }

    public UnoGameState StartGame(Guid requestingPlayerId, Random? random = null)
    {
        EnsureWaiting();
        EnsureCreator(requestingPlayerId);

        if (_players.Count < UnoGameFactory.MinimumPlayers)
        {
            throw new InvalidOperationException(
                $"At least {UnoGameFactory.MinimumPlayers} players are required to start.");
        }

        var game = UnoGameFactory.CreateGame(
            _players.Select(player => player.DisplayName).ToArray(),
            random,
            RuleSet);

        for (var index = 0; index < _players.Count; index++)
        {
            _players[index].GamePlayerId = game.Players[index].Id;
        }

        Game = game;
        Status = UnoRoomStatus.InProgress;
        return game;
    }

    private UnoRoomPlayer AddPlayer(string displayName, bool isCreator)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Player display name cannot be empty.",
                nameof(displayName));
        }

        var player = new UnoRoomPlayer(Guid.NewGuid(), displayName.Trim(), isCreator);
        _players.Add(player);
        return player;
    }

    private void EnsureWaiting()
    {
        if (Status != UnoRoomStatus.Waiting)
        {
            throw new InvalidOperationException(
                "Room settings and membership can only change before the game starts.");
        }
    }

    private void EnsureCreator(Guid requestingPlayerId)
    {
        if (requestingPlayerId != CreatorPlayerId)
        {
            throw new UnauthorizedAccessException(
                "Only the room creator can change settings or start the game.");
        }
    }

    private static string GenerateRoomCode()
    {
        return string.Create(6, RoomCodeAlphabet, (span, alphabet) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = alphabet[Random.Shared.Next(alphabet.Length)];
            }
        });
    }
}
