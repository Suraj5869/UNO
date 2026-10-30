namespace Uno.GameEngine;

/// <summary>
/// Applies player actions to a game state. The API layer should call this
/// class rather than changing game state directly.
/// </summary>
public sealed class UnoGameEngine
{
    private readonly Random _random;

    public UnoGameEngine(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public void PlayCard(
        UnoGameState game,
        Guid playerId,
        UnoCard card,
        CardColor chosenColor = CardColor.None)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(card);
        EnsureGameInProgress(game);
        EnsureCurrentPlayer(game, playerId);

        var player = game.CurrentPlayer;
        var isStackingDrawPenalty = game.PendingDrawAmount > 0;

        if (game.IsWildDrawFourChallengePending &&
            !(game.RuleSet == UnoRuleSet.House &&
              game.PendingDrawType == CardType.WildDrawFour))
        {
            throw new InvalidOperationException(
                "Resolve the Wild Draw Four challenge before playing a card.");
        }

        if (isStackingDrawPenalty &&
            (game.RuleSet != UnoRuleSet.House ||
             card.Type != game.PendingDrawType))
        {
            throw new InvalidOperationException(
                "Only the same draw-card type may be stacked under House Rules.");
        }

        if (!player.Hand.Contains(card))
        {
            throw new InvalidOperationException(
                "The selected card is not in the current player's hand.");
        }

        if (game.JustDrawnCard is not null &&
            game.JustDrawnCard != card)
        {
            throw new InvalidOperationException(
                "After drawing, only the drawn card may be played this turn.");
        }

        if (isStackingDrawPenalty && card.Type == game.PendingDrawType)
        {
            // Stacking is a House Rules exception to normal color matching.
        }
        else if (!IsPlayable(card, game.TopDiscard, game.ActiveColor))
        {
            throw new InvalidOperationException(
                "The selected card does not match the active color or top card.");
        }

        var isWild = card.Type is CardType.Wild or CardType.WildDrawFour;
        if (isWild && !IsPlayableColor(chosenColor))
        {
            throw new ArgumentException(
                "A wild card requires Red, Yellow, Green, or Blue as its chosen color.",
                nameof(chosenColor));
        }

        if (!isWild && chosenColor != CardColor.None)
        {
            throw new ArgumentException(
                "A color can only be selected when playing a wild card.",
                nameof(chosenColor));
        }

        var wildDrawFourWasIllegal =
            card.Type == CardType.WildDrawFour &&
            player.Hand.Any(handCard =>
                handCard.Color == game.ActiveColor &&
                handCard.Type is not (CardType.Wild or CardType.WildDrawFour));

        game.PlayerAwaitingUnoCallId = null;
        game.UnoCallDeadlineUtc = null;
        player.RemoveCard(card);
        if (player.Hand.Count == 1)
        {
            game.PlayerAwaitingUnoCallId = player.Id;
            game.UnoCallDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(3);
        }
        game.MutableDiscardPile.Add(card);
        game.JustDrawnCard = null;
        game.ActiveColor = isWild ? chosenColor : card.Color;

        if (card.Type == CardType.WildDrawFour)
        {
            game.PendingDrawType = CardType.WildDrawFour;
            game.PendingDrawAmount += 4;
            game.WildDrawFourOffenderIndex = game.CurrentPlayerIndex;
            game.WildDrawFourWasIllegal = wildDrawFourWasIllegal;
            AdvanceTurn(game);
            return;
        }

        switch (card.Type)
        {
            case CardType.Skip:
                AdvanceTurn(game, steps: 2);
                break;

            case CardType.Reverse:
                game.TurnDirection *= -1;
                AdvanceTurn(game, steps: game.Players.Count == 2 ? 0 : 1);
                break;

            case CardType.DrawTwo:
                if (game.RuleSet == UnoRuleSet.House)
                {
                    game.PendingDrawType = CardType.DrawTwo;
                    game.PendingDrawAmount += 2;
                    AdvanceTurn(game);
                }
                else
                {
                    AdvanceTurn(game);
                    DrawCards(game, 2);
                    AdvanceTurn(game);
                }
                break;

            default:
                AdvanceTurn(game);
                break;
        }

        if (player.Hand.Count == 0 && game.PendingDrawAmount == 0)
        {
            game.Status = GameStatus.Completed;
        }
    }

    /// <summary>
    /// Draws one card. If it is playable, the player may play that card or pass.
    /// If it is not playable, the player's turn ends automatically.
    /// </summary>
    public UnoCard DrawCard(UnoGameState game, Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(game);
        EnsureGameInProgress(game);
        EnsureCurrentPlayer(game, playerId);

        if (game.IsWildDrawFourChallengePending)
        {
            throw new InvalidOperationException(
                "Resolve the Wild Draw Four challenge before drawing.");
        }

        if (game.PendingDrawAmount > 0)
        {
            throw new InvalidOperationException(
                "Resolve the pending draw penalty before drawing a normal card.");
        }

        if (game.JustDrawnCard is not null)
        {
            throw new InvalidOperationException(
                "The player has already drawn a card this turn.");
        }

        game.PlayerAwaitingUnoCallId = null;
        var drawnCards = DrawCards(game, 1);
        if (drawnCards.Count == 0)
        {
            throw new InvalidOperationException(
                "No cards are available to draw.");
        }

        var card = drawnCards[0];
        if (IsPlayable(card, game.TopDiscard, game.ActiveColor))
        {
            game.JustDrawnCard = card;
        }
        else
        {
            AdvanceTurn(game);
        }

        return card;
    }

    /// <summary>
    /// Ends the current player's turn after they choose not to play
    /// a playable card they just drew.
    /// </summary>
    public void PassAfterDraw(UnoGameState game, Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(game);
        EnsureGameInProgress(game);
        EnsureCurrentPlayer(game, playerId);

        if (game.JustDrawnCard is null)
        {
            throw new InvalidOperationException(
                "The current player has no playable drawn card to pass on.");
        }

        game.JustDrawnCard = null;
        game.PlayerAwaitingUnoCallId = null;
        AdvanceTurn(game);
    }

    /// <summary>
    /// Accepts a Wild Draw Four penalty: the challenged player draws four
    /// cards and loses their turn.
    /// </summary>
    public void AcceptWildDrawFour(UnoGameState game, Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(game);
        EnsureGameInProgress(game);
        EnsureCurrentPlayer(game, playerId);
        EnsurePendingWildDrawFour(game);

        var offenderIndex = game.WildDrawFourOffenderIndex!.Value;
        var offenderHadNoCards = game.Players[offenderIndex].Hand.Count == 0;
        var penaltyAmount = game.PendingDrawAmount;

        game.PlayerAwaitingUnoCallId = null;
        DrawCards(game, penaltyAmount);
        ClearPendingDrawPenalty(game);

        if (offenderHadNoCards)
        {
            game.Status = GameStatus.Completed;
            return;
        }

        AdvanceTurn(game);
    }

    /// <summary>
    /// Challenges a Wild Draw Four. If the offender had a card matching
    /// the color active before the Wild Draw Four, the challenge succeeds
    /// and the offender draws four. Otherwise, the challenger draws six.
    /// </summary>
    public void ChallengeWildDrawFour(UnoGameState game, Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(game);
        EnsureGameInProgress(game);
        EnsureCurrentPlayer(game, playerId);
        EnsurePendingWildDrawFour(game);

        var offenderIndex = game.WildDrawFourOffenderIndex!.Value;
        var challengeSucceeded = game.WildDrawFourWasIllegal!.Value;
        var offenderHadNoCards = game.Players[offenderIndex].Hand.Count == 0;
        var penaltyAmount = game.PendingDrawAmount;

        ClearPendingDrawPenalty(game);
        game.PlayerAwaitingUnoCallId = null;

        if (challengeSucceeded)
        {
            DrawCards(game, penaltyAmount, offenderIndex);
            // The challenger keeps the turn after a successful challenge.
            return;
        }

        // A failed challenge adds the standard two-card challenge penalty
        // on top of any accumulated Wild Draw Four stack.
        DrawCards(game, penaltyAmount + 2);
        if (offenderHadNoCards)
        {
            game.Status = GameStatus.Completed;
            return;
        }

        AdvanceTurn(game);
    }

    /// <summary>
    /// Records that a player has called UNO after being reduced to one card.
    /// This must happen before the next player begins an action.
    /// </summary>
    public void CallUno(UnoGameState game, Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(game);
        EnsureGameInProgress(game);

        if (game.PlayerAwaitingUnoCallId != playerId)
        {
            throw new InvalidOperationException(
                "This player does not have an outstanding UNO call.");
        }

        var player = game.Players.FirstOrDefault(candidate => candidate.Id == playerId);
        if (player is null || player.Hand.Count != 1)
        {
            throw new InvalidOperationException(
                "UNO can only be called when the player has exactly one card.");
        }

        game.PlayerAwaitingUnoCallId = null;
        game.UnoCallDeadlineUtc = null;
    }

    /// <summary>
    /// Catches a player who has one card but has not called UNO. Any other
    /// participant may catch them while the call window remains open.
    /// </summary>
    public void CatchUno(
        UnoGameState game,
        Guid catcherPlayerId,
        Guid targetPlayerId)
    {
        ArgumentNullException.ThrowIfNull(game);
        EnsureGameInProgress(game);

        if (game.PlayerAwaitingUnoCallId != targetPlayerId)
        {
            throw new InvalidOperationException(
                "There is no outstanding missed UNO call for that player.");
        }

        if (game.UnoCallDeadlineUtc is { } deadline && DateTimeOffset.UtcNow < deadline)
        {
            throw new InvalidOperationException("Give the player three seconds to call UNO before catching them.");
        }

        if (catcherPlayerId == targetPlayerId ||
            !game.Players.Any(player => player.Id == catcherPlayerId))
        {
            throw new InvalidOperationException(
                "A different player in this game must catch the missed UNO call.");
        }

        var targetIndex = -1;
        for (var index = 0; index < game.Players.Count; index++)
        {
            if (game.Players[index].Id == targetPlayerId)
            {
                targetIndex = index;
                break;
            }
        }

        if (targetIndex < 0 || game.Players[targetIndex].Hand.Count != 1)
        {
            throw new InvalidOperationException(
                "The target player must have exactly one card.");
        }

        game.PlayerAwaitingUnoCallId = null;
        game.UnoCallDeadlineUtc = null;
        DrawCards(game, 2, targetIndex);
    }

    private static bool IsPlayable(
        UnoCard card,
        UnoCard topDiscard,
        CardColor activeColor)
    {
        if (card.Type is CardType.Wild or CardType.WildDrawFour)
        {
            return true;
        }

        return card.Color == activeColor ||
               (card.Type == CardType.Number &&
                topDiscard.Type == CardType.Number &&
                card.Number == topDiscard.Number) ||
               (card.Type != CardType.Number &&
                card.Type == topDiscard.Type);
    }

    private static bool IsPlayableColor(CardColor color) =>
        color is CardColor.Red or CardColor.Yellow
            or CardColor.Green or CardColor.Blue;

    private static void EnsureGameInProgress(UnoGameState game)
    {
        if (game.Status != GameStatus.InProgress)
        {
            throw new InvalidOperationException(
                "The game has already completed.");
        }
    }

    private static void EnsureCurrentPlayer(UnoGameState game, Guid playerId)
    {
        if (game.PlayerAwaitingUnoCallId is not null &&
            game.PlayerAwaitingUnoCallId != playerId &&
            game.UnoCallDeadlineUtc is { } deadline && DateTimeOffset.UtcNow < deadline)
        {
            throw new InvalidOperationException("Wait up to three seconds for the player to call UNO.");
        }

        if (game.CurrentPlayer.Id != playerId)
        {
            throw new InvalidOperationException(
                "Only the current player may perform this action.");
        }
    }

    private static void EnsurePendingWildDrawFour(UnoGameState game)
    {
        if (!game.IsWildDrawFourChallengePending)
        {
            throw new InvalidOperationException(
                "There is no pending Wild Draw Four to accept or challenge.");
        }
    }

    /// <summary>
    /// Accepts a pending House Rules Draw Two stack and loses the turn.
    /// </summary>
    public void AcceptDrawPenalty(UnoGameState game, Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(game);
        EnsureGameInProgress(game);
        EnsureCurrentPlayer(game, playerId);

        if (game.PendingDrawAmount <= 0 || game.PendingDrawType is null)
        {
            throw new InvalidOperationException(
                "There is no pending draw penalty to accept.");
        }

        if (game.IsWildDrawFourChallengePending)
        {
            throw new InvalidOperationException(
                "A Wild Draw Four must be accepted or challenged.");
        }

        var penaltyAmount = game.PendingDrawAmount;
        game.PlayerAwaitingUnoCallId = null;
        ClearPendingDrawPenalty(game);
        DrawCards(game, penaltyAmount);
        AdvanceTurn(game);
    }

    private static void ClearPendingDrawPenalty(UnoGameState game)
    {
        game.PendingDrawType = null;
        game.PendingDrawAmount = 0;
        game.WildDrawFourOffenderIndex = null;
        game.WildDrawFourWasIllegal = null;
    }

    /// <summary>Automatically resolves an expired turn and advances play.</summary>
    public bool AutoResolveTimedOutTurn(UnoGameState game, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Status != GameStatus.InProgress || now < game.TurnDeadlineUtc) return false;
        game.PlayerAwaitingUnoCallId = null;
        game.UnoCallDeadlineUtc = null;
        game.JustDrawnCard = null;
        var wildDrawFourOffenderIndex = game.WildDrawFourOffenderIndex;
        var offenderHadNoCards = wildDrawFourOffenderIndex.HasValue &&
            game.Players[wildDrawFourOffenderIndex.Value].Hand.Count == 0;
        var cardsToDraw = game.PendingDrawAmount > 0 ? game.PendingDrawAmount : 1;
        ClearPendingDrawPenalty(game);
        DrawCards(game, cardsToDraw);

        if (offenderHadNoCards)
        {
            game.Status = GameStatus.Completed;
            return true;
        }

        AdvanceTurn(game);
        return true;
    }

    private void AdvanceTurn(UnoGameState game, int steps = 1)
    {
        for (var i = 0; i < steps; i++)
        {
            game.CurrentPlayerIndex =
                (game.CurrentPlayerIndex + game.TurnDirection + game.Players.Count)
                % game.Players.Count;
        }
        game.TurnDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(UnoGameState.TurnDuration);
    }

    private List<UnoCard> DrawCards(
        UnoGameState game,
        int count,
        int? playerIndex = null)
    {
        var drawn = new List<UnoCard>(count);
        for (var i = 0; i < count; i++)
        {
            if (game.MutableDrawPile.Count == 0)
            {
                ReplenishDrawPile(game);
            }

            if (game.MutableDrawPile.Count == 0)
            {
                break;
            }

            var lastIndex = game.MutableDrawPile.Count - 1;
            var card = game.MutableDrawPile[lastIndex];
            game.MutableDrawPile.RemoveAt(lastIndex);
            game.MutablePlayers[playerIndex ?? game.CurrentPlayerIndex].AddCard(card);
            drawn.Add(card);
        }

        return drawn;
    }

    private void ReplenishDrawPile(UnoGameState game)
    {
        var discardPile = game.MutableDiscardPile;
        if (discardPile.Count <= 1)
        {
            return;
        }

        // Recycle every discard except the top card, which must remain playable.
        var recyclableCards = discardPile.Take(discardPile.Count - 1).ToList();
        discardPile.RemoveRange(0, discardPile.Count - 1);

        for (var i = recyclableCards.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (recyclableCards[i], recyclableCards[j]) =
                (recyclableCards[j], recyclableCards[i]);
        }

        game.MutableDrawPile.AddRange(recyclableCards);
    }
}
