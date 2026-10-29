namespace Uno.GameEngine;

public sealed class UnoGameState
{
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
    }

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

    public bool IsWildDrawFourChallengePending =>
        WildDrawFourOffenderIndex.HasValue;

    public UnoPlayer CurrentPlayer =>
        Players[CurrentPlayerIndex];

    public UnoCard TopDiscard =>
        _discardPile[^1];

    internal UnoCard? JustDrawnCard { get; set; }

    internal int? WildDrawFourOffenderIndex { get; set; }

    internal bool? WildDrawFourWasIllegal { get; set; }

    /// <summary>Type of draw card currently being stacked under House Rules.</summary>
    public CardType? PendingDrawType { get; internal set; }

    /// <summary>Total cards owed by the current pending draw-card chain.</summary>
    public int PendingDrawAmount { get; internal set; }

    internal List<UnoCard> MutableDrawPile => _drawPile;

    internal List<UnoCard> MutableDiscardPile => _discardPile;

    internal List<UnoPlayer> MutablePlayers => _players;
}
