namespace Uno.GameEngine;

public sealed class UnoGameState
{
    public const int TurnDuration = 30;
    private readonly List<UnoPlayer> _players;
    private readonly List<UnoCard> _drawPile;
    private readonly List<UnoCard> _discardPile;

    internal UnoGameState(
        IEnumerable<UnoPlayer> players,
        IEnumerable<UnoCard> drawPile,
        IEnumerable<UnoCard> discardPile,
        CardColor activeColor,
        int currentPlayerIndex,
        int turnDirection,
        GameStatus status,
        UnoRuleSet ruleSet)
    {
        _players = players.ToList();
        _drawPile = drawPile.ToList();
        _discardPile = discardPile.ToList();

        if (_players.Count == 0)
        {
            throw new ArgumentException(
                "A game must contain at least one player.",
                nameof(players));
        }

        if (currentPlayerIndex < 0 ||
            currentPlayerIndex >= _players.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentPlayerIndex));
        }

        if (turnDirection is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(turnDirection),
                "Turn direction must be 1 or -1.");
        }

        Players = _players.AsReadOnly();
        ActiveColor = activeColor;
        CurrentPlayerIndex = currentPlayerIndex;
        TurnDirection = turnDirection;
        Status = status;
        RuleSet = ruleSet;
        TurnDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(TurnDuration);
    }

    public Guid MatchId { get; private set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;
    public int CardsPlayed { get; internal set; }
    public IReadOnlyList<UnoPlayer> Players { get; }

    public IReadOnlyList<UnoCard> DrawPile =>
        _drawPile.AsReadOnly();

    public IReadOnlyList<UnoCard> DiscardPile =>
        _discardPile.AsReadOnly();

    public CardColor ActiveColor { get; internal set; }

    public int CurrentPlayerIndex { get; internal set; }

    public int TurnDirection { get; internal set; }

    public GameStatus Status { get; internal set; }

    /// <summary>
    /// Rules selected when this game was created. This value is immutable
    /// for the lifetime of the game so clients cannot switch rules mid-match.
    /// </summary>
    public UnoRuleSet RuleSet { get; }

    /// <summary>
    /// Player who has one card and has not yet called UNO. The opportunity
    /// expires when the next player begins an action.
    /// </summary>
    public Guid? PlayerAwaitingUnoCallId { get; internal set; }

    /// <summary>Earliest time opponents may catch a missed UNO call.</summary>
    public DateTimeOffset? UnoCallDeadlineUtc { get; internal set; }

    public bool IsWildDrawFourChallengePending =>
        WildDrawFourOffenderIndex.HasValue;

    public UnoPlayer CurrentPlayer =>
        Players[CurrentPlayerIndex];

    public UnoCard TopDiscard =>
        _discardPile[^1];

    internal UnoCard? JustDrawnCard { get; set; }
    public bool CanPassAfterDraw => JustDrawnCard is not null;
    public DateTimeOffset TurnDeadlineUtc { get; internal set; }
    public UnoCard? JustDrawnCardForCurrentPlayer => JustDrawnCard;

    internal int? WildDrawFourOffenderIndex { get; set; }

    internal bool? WildDrawFourWasIllegal { get; set; }

    /// <summary>Type of draw card currently being stacked under House Rules.</summary>
    public CardType? PendingDrawType { get; internal set; }

    /// <summary>Total cards owed by the current pending draw-card chain.</summary>
    public int PendingDrawAmount { get; internal set; }

    public sealed record PlayerCheckpoint(Guid Id,string Name,UnoCard[] Hand);
    public sealed record Checkpoint(Guid MatchId,DateTimeOffset StartedAt,int CardsPlayed,PlayerCheckpoint[] Players,UnoCard[] Draw,UnoCard[] Discard,CardColor Color,int Index,int Direction,GameStatus Status,UnoRuleSet Rules,Guid? AwaitingUno,DateTimeOffset? UnoDeadline,UnoCard? JustDrawn,DateTimeOffset TurnDeadline,int? Offender,bool? Illegal,CardType? PendingType,int PendingAmount);
    public Checkpoint ExportCheckpoint() => new(MatchId,StartedAt,CardsPlayed,Players.Select(p=>new PlayerCheckpoint(p.Id,p.Name,p.Hand.ToArray())).ToArray(),DrawPile.ToArray(),DiscardPile.ToArray(),ActiveColor,CurrentPlayerIndex,TurnDirection,Status,RuleSet,PlayerAwaitingUnoCallId,UnoCallDeadlineUtc,JustDrawnCard,TurnDeadlineUtc,WildDrawFourOffenderIndex,WildDrawFourWasIllegal,PendingDrawType,PendingDrawAmount);
    public static UnoGameState RestoreCheckpoint(Checkpoint c)
    {
        var players=c.Players.Select(p=> { var player=new UnoPlayer(p.Name,p.Id); foreach(var card in p.Hand) player.AddCard(card); return player; });
        return new UnoGameState(players,c.Draw,c.Discard,c.Color,c.Index,c.Direction,c.Status,c.Rules) {
            MatchId=c.MatchId,StartedAt=c.StartedAt,CardsPlayed=c.CardsPlayed,PlayerAwaitingUnoCallId=c.AwaitingUno,UnoCallDeadlineUtc=c.UnoDeadline,JustDrawnCard=c.JustDrawn,TurnDeadlineUtc=c.TurnDeadline,WildDrawFourOffenderIndex=c.Offender,WildDrawFourWasIllegal=c.Illegal,PendingDrawType=c.PendingType,PendingDrawAmount=c.PendingAmount };
    }

    internal List<UnoCard> MutableDrawPile => _drawPile;

    internal List<UnoCard> MutableDiscardPile => _discardPile;

    internal List<UnoPlayer> MutablePlayers => _players;
}
