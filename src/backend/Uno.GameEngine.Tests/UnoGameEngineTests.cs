using Xunit;

namespace Uno.GameEngine.Tests;

public sealed class UnoGameEngineTests
{
    [Fact]
    public void CreateGame_WithoutRuleSelection_UsesOfficialRules()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob"],
            new Random(123));

        Assert.Equal(UnoRuleSet.Official, game.RuleSet);
    }

    [Fact]
    public void CreateGame_WithHouseRules_PersistsSelectedRuleSet()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob", "Charlie"],
            new Random(123),
            UnoRuleSet.House);

        Assert.Equal(UnoRuleSet.House, game.RuleSet);
    }

    [Fact]
    public void CreateGame_WithUnknownRuleSet_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UnoGameFactory.CreateGame(
                ["Alice", "Bob"],
                new Random(123),
                (UnoRuleSet)999));
    }

    [Fact]
    public void PlayCard_WithPlayableNumberCard_UpdatesDiscardAndTurn()
    {
        var game = CreateGameWithPlayableNumberCard(out var card);
        var engine = new UnoGameEngine();

        var currentPlayer = game.CurrentPlayer;
        var nextPlayerIndex = (game.CurrentPlayerIndex + 1) % game.Players.Count;
        var originalHandSize = currentPlayer.Hand.Count;

        engine.PlayCard(game, currentPlayer.Id, card);

        Assert.Equal(card, game.TopDiscard);
        Assert.DoesNotContain(card, currentPlayer.Hand);
        Assert.Equal(originalHandSize - 1, currentPlayer.Hand.Count);
        Assert.Equal(nextPlayerIndex, game.CurrentPlayerIndex);
        Assert.Equal(GameStatus.InProgress, game.Status);
    }

    [Fact]
    public void PlayCard_WithUnmatchedCard_ThrowsAndDoesNotChangeState()
    {
        var game = CreateGameWithUnplayableCard(out var card);
        var engine = new UnoGameEngine();
        var currentPlayer = game.CurrentPlayer;
        var originalHandSize = currentPlayer.Hand.Count;
        var originalTopCard = game.TopDiscard;

        Assert.Throws<InvalidOperationException>(() =>
            engine.PlayCard(game, currentPlayer.Id, card));

        Assert.Equal(originalTopCard, game.TopDiscard);
        Assert.Equal(originalHandSize, currentPlayer.Hand.Count);
    }

    [Fact]
    public void PlayCard_ByNonCurrentPlayer_Throws()
    {
        var game = CreateGameWithPlayableNumberCard(out var card);
        var engine = new UnoGameEngine();
        var nonCurrentPlayer = game.Players[1];

        Assert.Throws<InvalidOperationException>(() =>
            engine.PlayCard(game, nonCurrentPlayer.Id, card));
    }

    [Fact]
    public void PlayCard_WithWildCardWithoutChosenColor_Throws()
    {
        var game = CreateGameWithCurrentPlayerCard(
            CardType.Wild,
            out var card);
        var engine = new UnoGameEngine();
        var currentPlayer = game.CurrentPlayer;

        Assert.Throws<ArgumentException>(() =>
            engine.PlayCard(game, currentPlayer.Id, card));

        Assert.Contains(card, currentPlayer.Hand);
    }

    [Fact]
    public void PlayReverse_WithTwoPlayers_GivesTheTurnBackToTheSamePlayer()
    {
        var game = CreateGameWithPlayableActionCard(
            CardType.Reverse,
            playerCount: 2,
            out var card);
        var engine = new UnoGameEngine();
        var currentPlayer = game.CurrentPlayer;

        engine.PlayCard(game, currentPlayer.Id, card);

        Assert.Equal(-1, game.TurnDirection);
        Assert.Equal(currentPlayer.Id, game.CurrentPlayer.Id);
    }

    [Fact]
    public void PlayDrawTwo_MakesNextPlayerDrawTwoAndSkipTheirTurn()
    {
        var game = CreateGameWithPlayableActionCard(
            CardType.DrawTwo,
            playerCount: 3,
            out var card);
        var engine = new UnoGameEngine();
        var currentPlayer = game.CurrentPlayer;
        var penalizedPlayer = game.Players[1];
        var originalHandSize = penalizedPlayer.Hand.Count;

        engine.PlayCard(game, currentPlayer.Id, card);

        Assert.Equal(originalHandSize + 2, penalizedPlayer.Hand.Count);
        Assert.Equal(game.Players[2].Id, game.CurrentPlayer.Id);
    }

    [Fact]
    public void PlayDrawTwo_UnderHouseRules_CreatesStackAndNextPlayerCanStack()
    {
        var game = CreateHouseGameWithStackableDrawTwos(out var first, out var second);
        var engine = new UnoGameEngine();
        var firstPlayer = game.CurrentPlayer;
        var secondPlayer = game.Players[1];
        var secondOriginalHandSize = secondPlayer.Hand.Count;

        engine.PlayCard(game, firstPlayer.Id, first);

        Assert.Equal(CardType.DrawTwo, game.PendingDrawType);
        Assert.Equal(2, game.PendingDrawAmount);
        Assert.Equal(secondPlayer.Id, game.CurrentPlayer.Id);
        Assert.Equal(secondOriginalHandSize, secondPlayer.Hand.Count);

        engine.PlayCard(game, secondPlayer.Id, second);

        Assert.Equal(4, game.PendingDrawAmount);
        Assert.Equal(game.Players[2].Id, game.CurrentPlayer.Id);
        Assert.Equal(secondOriginalHandSize - 1, secondPlayer.Hand.Count);
    }

    [Fact]
    public void AcceptDrawPenalty_DrawsAccumulatedDrawTwoStackAndSkipsTurn()
    {
        var game = CreateHouseGameWithStackableDrawTwos(out var first, out var second);
        var engine = new UnoGameEngine();
        engine.PlayCard(game, game.CurrentPlayer.Id, first);
        engine.PlayCard(game, game.CurrentPlayer.Id, second);

        var penalizedPlayer = game.CurrentPlayer;
        var originalHandSize = penalizedPlayer.Hand.Count;
        // Alice and Bob have played the two stacked cards, so Charlie accepts
        // the penalty and skips their turn; play returns to Alice.
        var nextPlayer = game.Players[0];

        engine.AcceptDrawPenalty(game, penalizedPlayer.Id);

        Assert.Equal(originalHandSize + 4, penalizedPlayer.Hand.Count);
        Assert.Equal(nextPlayer.Id, game.CurrentPlayer.Id);
        Assert.Equal(0, game.PendingDrawAmount);
        Assert.Null(game.PendingDrawType);
    }

    [Fact]
    public void PlayDrawTwo_UnderHouseRules_RejectsMixedStacking()
    {
        var game = CreateHouseGameWithStackableDrawTwosAndWild(out var first, out var wild);
        var engine = new UnoGameEngine();
        engine.PlayCard(game, game.CurrentPlayer.Id, first);
        var penalizedPlayer = game.CurrentPlayer;

        Assert.Throws<InvalidOperationException>(() =>
            engine.PlayCard(game, penalizedPlayer.Id, wild, CardColor.Red));
        Assert.Equal(2, game.PendingDrawAmount);
    }

    [Fact]
    public void PlayWildDrawFour_UnderHouseRules_CanStackAndAccumulatesPenalty()
    {
        var game = CreateHouseGameWithStackableWildDrawFours(out var first, out var second);
        var engine = new UnoGameEngine();

        engine.PlayCard(game, game.CurrentPlayer.Id, first, CardColor.Red);
        Assert.Equal(4, game.PendingDrawAmount);

        engine.PlayCard(game, game.CurrentPlayer.Id, second, CardColor.Blue);

        Assert.Equal(CardType.WildDrawFour, game.PendingDrawType);
        Assert.Equal(8, game.PendingDrawAmount);
        Assert.True(game.IsWildDrawFourChallengePending);
        Assert.Equal(game.Players[2].Id, game.CurrentPlayer.Id);
    }

    [Fact]
    public void PlayWildDrawFour_CreatesPendingChallengeForNextPlayer()
    {
        var game = CreateGameWithCurrentPlayerCard(
            CardType.WildDrawFour,
            out var card);
        var engine = new UnoGameEngine();
        var offender = game.CurrentPlayer;
        var expectedChallengerIndex =
            (game.CurrentPlayerIndex + 1) % game.Players.Count;

        engine.PlayCard(game, offender.Id, card, CardColor.Green);

        Assert.True(game.IsWildDrawFourChallengePending);
        Assert.Equal(expectedChallengerIndex, game.CurrentPlayerIndex);
        Assert.Equal(CardColor.Green, game.ActiveColor);
    }

    [Fact]
    public void AcceptWildDrawFour_DrawsFourCardsAndMovesTurnOn()
    {
        var game = CreateGameWithCurrentPlayerCard(
            CardType.WildDrawFour,
            out var card);
        var engine = new UnoGameEngine();
        var offender = game.CurrentPlayer;

        engine.PlayCard(game, offender.Id, card, CardColor.Green);

        var challenger = game.CurrentPlayer;
        var challengerHandSize = challenger.Hand.Count;

        engine.AcceptWildDrawFour(game, challenger.Id);

        Assert.False(game.IsWildDrawFourChallengePending);
        Assert.Equal(challengerHandSize + 4, challenger.Hand.Count);
        Assert.NotEqual(challenger.Id, game.CurrentPlayer.Id);
    }

    [Fact]
    public void ChallengeWildDrawFour_WhenIllegal_MakesOffenderDrawFourAndKeepsChallengerTurn()
    {
        var game = CreateGameWithWildDrawFourLegality(
            shouldBeIllegal: true,
            out var card);
        var engine = new UnoGameEngine();
        var offender = game.CurrentPlayer;
        var offenderHandSizeAfterPlay = offender.Hand.Count - 1;

        engine.PlayCard(game, offender.Id, card, CardColor.Green);

        var challenger = game.CurrentPlayer;
        var challengerHandSize = challenger.Hand.Count;

        engine.ChallengeWildDrawFour(game, challenger.Id);

        Assert.Equal(offenderHandSizeAfterPlay + 4, offender.Hand.Count);
        Assert.Equal(challengerHandSize, challenger.Hand.Count);
        Assert.Equal(challenger.Id, game.CurrentPlayer.Id);
        Assert.False(game.IsWildDrawFourChallengePending);
    }

    [Fact]
    public void ChallengeWildDrawFour_WhenLegal_MakesChallengerDrawSixAndLoseTurn()
    {
        var game = CreateGameWithWildDrawFourLegality(
            shouldBeIllegal: false,
            out var card);
        var engine = new UnoGameEngine();
        var offender = game.CurrentPlayer;

        engine.PlayCard(game, offender.Id, card, CardColor.Green);

        var challenger = game.CurrentPlayer;
        var challengerHandSize = challenger.Hand.Count;
        var nextPlayer = game.Players[2];

        engine.ChallengeWildDrawFour(game, challenger.Id);

        Assert.Equal(challengerHandSize + 6, challenger.Hand.Count);
        Assert.Equal(nextPlayer.Id, game.CurrentPlayer.Id);
        Assert.False(game.IsWildDrawFourChallengePending);
    }

    [Fact]
    public void PlayCard_LeavingOneCard_OpensUnoCallWindow()
    {
        var game = CreateGameWithTwoCardCurrentPlayer(out var card);
        var engine = new UnoGameEngine();
        var player = game.CurrentPlayer;

        engine.PlayCard(game, player.Id, card);

        Assert.Equal(player.Id, game.PlayerAwaitingUnoCallId);

        engine.CallUno(game, player.Id);

        Assert.Null(game.PlayerAwaitingUnoCallId);
    }

    [Fact]
    public void CatchUno_WhenCallWasMissed_TargetDrawsTwoCards()
    {
        var game = CreateGameWithTwoCardCurrentPlayer(out var card);
        var engine = new UnoGameEngine();
        var target = game.CurrentPlayer;

        engine.PlayCard(game, target.Id, card);

        var catcher = game.CurrentPlayer;
        var originalTargetHandSize = target.Hand.Count;

        Assert.Throws<InvalidOperationException>(() =>
            engine.CatchUno(game, catcher.Id, target.Id));
        Thread.Sleep(3100);
        engine.CatchUno(game, catcher.Id, target.Id);

        Assert.Equal(originalTargetHandSize + 2, target.Hand.Count);
        Assert.Null(game.PlayerAwaitingUnoCallId);
        Assert.Equal(catcher.Id, game.CurrentPlayer.Id);
    }

    [Fact]
    public void DrawCard_ByNextPlayer_ClosesPreviousPlayersUnoCallWindow()
    {
        var game = CreateGameWithTwoCardCurrentPlayer(out var card);
        var engine = new UnoGameEngine();
        var previousPlayer = game.CurrentPlayer;

        engine.PlayCard(game, previousPlayer.Id, card);
        Assert.Equal(previousPlayer.Id, game.PlayerAwaitingUnoCallId);

        var nextPlayer = game.CurrentPlayer;
        Thread.Sleep(3100);
        engine.DrawCard(game, nextPlayer.Id);

        Assert.Null(game.PlayerAwaitingUnoCallId);
    }

    [Fact]
    public void CatchUno_AfterCallWindowExpires_Throws()
    {
        var game = CreateGameWithTwoCardCurrentPlayer(out var card);
        var engine = new UnoGameEngine();
        var target = game.CurrentPlayer;

        engine.PlayCard(game, target.Id, card);
        var catcher = game.CurrentPlayer;
        engine.DrawCard(game, catcher.Id);

        Assert.Throws<InvalidOperationException>(() =>
            engine.CatchUno(game, catcher.Id, target.Id));
    }

    private static UnoGameState CreateHouseGameWithStackableDrawTwos(
        out UnoCard firstDrawTwo,
        out UnoCard secondDrawTwo)
    {
        for (var seed = 0; seed < 2000; seed++)
        {
            var game = UnoGameFactory.CreateGame(
                ["Alice", "Bob", "Charlie"], new Random(seed), UnoRuleSet.House);
            var first = game.CurrentPlayer.Hand.FirstOrDefault(card =>
                card.Type == CardType.DrawTwo && card.Color == game.ActiveColor);
            var second = game.Players[1].Hand.FirstOrDefault(card =>
                card.Type == CardType.DrawTwo);
            if (first is not null && second is not null)
            {
                firstDrawTwo = first;
                secondDrawTwo = second;
                return game;
            }
        }

        throw new InvalidOperationException("Could not create a game with stackable Draw Two cards.");
    }

    private static UnoGameState CreateHouseGameWithStackableDrawTwosAndWild(
        out UnoCard firstDrawTwo,
        out UnoCard wildCard)
    {
        for (var seed = 0; seed < 2000; seed++)
        {
            var game = UnoGameFactory.CreateGame(
                ["Alice", "Bob", "Charlie"], new Random(seed), UnoRuleSet.House);
            var first = game.CurrentPlayer.Hand.FirstOrDefault(card =>
                card.Type == CardType.DrawTwo && card.Color == game.ActiveColor);
            var wild = game.Players[1].Hand.FirstOrDefault(card =>
                card.Type == CardType.Wild);
            if (first is not null && wild is not null)
            {
                firstDrawTwo = first;
                wildCard = wild;
                return game;
            }
        }

        throw new InvalidOperationException("Could not create a game with a Draw Two and a Wild in the next hand.");
    }

    private static UnoGameState CreateHouseGameWithStackableWildDrawFours(
        out UnoCard firstWildDrawFour,
        out UnoCard secondWildDrawFour)
    {
        for (var seed = 0; seed < 3000; seed++)
        {
            var game = UnoGameFactory.CreateGame(
                ["Alice", "Bob", "Charlie"], new Random(seed), UnoRuleSet.House);
            var first = game.CurrentPlayer.Hand.FirstOrDefault(card =>
                card.Type == CardType.WildDrawFour);
            var second = game.Players[1].Hand.FirstOrDefault(card =>
                card.Type == CardType.WildDrawFour);
            if (first is not null && second is not null)
            {
                firstWildDrawFour = first;
                secondWildDrawFour = second;
                return game;
            }
        }

        throw new InvalidOperationException("Could not create a game with stackable Wild Draw Four cards.");
    }

    private static UnoGameState CreateGameWithPlayableNumberCard(
        out UnoCard card)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var game = UnoGameFactory.CreateGame(
                ["Alice", "Bob", "Charlie"],
                new Random(seed));

            var candidate = game.CurrentPlayer.Hand.FirstOrDefault(handCard =>
                handCard.Type == CardType.Number &&
                (handCard.Color == game.ActiveColor ||
                 handCard.Number == game.TopDiscard.Number));

            if (candidate is not null)
            {
                card = candidate;
                return game;
            }
        }

        throw new InvalidOperationException(
            "Could not create a game with a playable number card.");
    }

    private static UnoGameState CreateGameWithUnplayableCard(
        out UnoCard card)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var game = UnoGameFactory.CreateGame(
                ["Alice", "Bob", "Charlie"],
                new Random(seed));

            var candidate = game.CurrentPlayer.Hand.FirstOrDefault(handCard =>
                handCard.Type is not (CardType.Wild or CardType.WildDrawFour) &&
                handCard.Color != game.ActiveColor &&
                !(handCard.Type == CardType.Number &&
                  handCard.Number == game.TopDiscard.Number));

            if (candidate is not null)
            {
                card = candidate;
                return game;
            }
        }

        throw new InvalidOperationException(
            "Could not create a game with an unplayable card.");
    }

    private static UnoGameState CreateGameWithCurrentPlayerCard(
        CardType cardType,
        out UnoCard card)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var game = UnoGameFactory.CreateGame(
                ["Alice", "Bob", "Charlie"],
                new Random(seed));

            var candidate = game.CurrentPlayer.Hand.FirstOrDefault(
                handCard => handCard.Type == cardType);

            if (candidate is not null)
            {
                card = candidate;
                return game;
            }
        }

        throw new InvalidOperationException(
            $"Could not create a game with a {cardType} card in the current player's hand.");
    }

    private static UnoGameState CreateGameWithWildDrawFourLegality(
        bool shouldBeIllegal,
        out UnoCard card)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var game = UnoGameFactory.CreateGame(
                ["Alice", "Bob", "Charlie"],
                new Random(seed));

            var candidate = game.CurrentPlayer.Hand.FirstOrDefault(
                handCard => handCard.Type == CardType.WildDrawFour);

            if (candidate is null)
            {
                continue;
            }

            var hasMatchingColor = game.CurrentPlayer.Hand.Any(handCard =>
                handCard != candidate &&
                handCard.Color == game.ActiveColor &&
                handCard.Type is not (CardType.Wild or CardType.WildDrawFour));

            if (hasMatchingColor == shouldBeIllegal)
            {
                card = candidate;
                return game;
            }
        }

        throw new InvalidOperationException(
            "Could not create the requested Wild Draw Four challenge scenario.");
    }

    private static UnoGameState CreateGameWithPlayableActionCard(
        CardType cardType,
        int playerCount,
        out UnoCard card)
    {
        var names = Enumerable.Range(1, playerCount)
            .Select(number => $"Player {number}")
            .ToArray();

        for (var seed = 0; seed < 500; seed++)
        {
            var game = UnoGameFactory.CreateGame(names, new Random(seed));
            var candidate = game.CurrentPlayer.Hand.FirstOrDefault(handCard =>
                handCard.Type == cardType &&
                handCard.Color == game.ActiveColor);

            if (candidate is not null)
            {
                card = candidate;
                return game;
            }
        }

        throw new InvalidOperationException(
            $"Could not create a game with a playable {cardType} card.");
    }

    private static UnoGameState CreateGameWithTwoCardCurrentPlayer(
        out UnoCard card)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var game = UnoGameFactory.CreateGame(
                ["Alice", "Bob", "Charlie"],
                new Random(seed));
            var player = game.CurrentPlayer;

            var candidate = player.Hand.FirstOrDefault(handCard =>
                handCard.Type == CardType.Number &&
                (handCard.Color == game.ActiveColor ||
                 handCard.Number == game.TopDiscard.Number));

            if (candidate is null)
            {
                continue;
            }

            var secondCard = player.Hand.FirstOrDefault(
                handCard => handCard != candidate);
            if (secondCard is null)
            {
                continue;
            }

            foreach (var handCard in player.Hand.ToList())
            {
                if (handCard != candidate && handCard != secondCard)
                {
                    player.RemoveCard(handCard);
                }
            }

            card = candidate;
            return game;
        }

        throw new InvalidOperationException(
            "Could not create a game with a playable card and exactly two cards in hand.");
    }

}
