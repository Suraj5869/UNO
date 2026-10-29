using Xunit;

namespace Uno.GameEngine.Tests;

public sealed class UnoGameEngineTests
{
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
}
