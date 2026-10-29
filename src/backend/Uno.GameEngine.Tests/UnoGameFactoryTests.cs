using Xunit;

namespace Uno.GameEngine.Tests;

public class UnoGameFactoryTests
{
    [Fact]
    public void CreateGame_WithTwoPlayers_CreatesValidGame()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob"],
            new Random(123));

        Assert.Equal(2, game.Players.Count);
        Assert.Equal(GameStatus.InProgress, game.Status);
        Assert.Equal("Alice", game.CurrentPlayer.Name);
    }

    [Fact]
    public void CreateGame_WithTenPlayers_IsSupported()
    {
        var names = Enumerable.Range(1, 10)
            .Select(number => $"Player {number}")
            .ToArray();

        var game = UnoGameFactory.CreateGame(
            names,
            new Random(123));

        Assert.Equal(10, game.Players.Count);
        Assert.All(
            game.Players,
            player => Assert.Equal(7, player.Hand.Count));
    }

    [Fact]
    public void CreateGame_DealsSevenCardsToEveryPlayer()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob", "Charlie", "Diana"],
            new Random(123));

        Assert.All(
            game.Players,
            player => Assert.Equal(7, player.Hand.Count));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void CreateGame_WithInvalidPlayerCount_Throws(
        int playerCount)
    {
        var names = Enumerable.Range(1, playerCount)
            .Select(number => $"Player {number}")
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => UnoGameFactory.CreateGame(
                names,
                new Random(123)));
    }

    [Fact]
    public void CreateGame_WithNullPlayerNames_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => UnoGameFactory.CreateGame(null!));
    }

    [Fact]
    public void CreateGame_WithBlankPlayerName_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => UnoGameFactory.CreateGame(
                ["Alice", " "]));
    }

    [Fact]
    public void CreateGame_StartsWithANumberCard()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob"],
            new Random(123));

        Assert.Equal(CardType.Number, game.TopDiscard.Type);
        Assert.NotNull(game.TopDiscard.Number);
    }

    [Fact]
    public void CreateGame_ActiveColorMatchesStartingCard()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob"],
            new Random(123));

        Assert.Equal(
            game.TopDiscard.Color,
            game.ActiveColor);

        Assert.NotEqual(CardColor.None, game.ActiveColor);
    }

    [Fact]
    public void CreateGame_StartsWithForwardDirection()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob"],
            new Random(123));

        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(1, game.TurnDirection);
    }

    [Fact]
    public void CreateGame_AccountsForAll108Cards()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob", "Charlie"],
            new Random(123));

        var cardsInHands = game.Players
            .Sum(player => player.Hand.Count);

        var totalCards =
            cardsInHands +
            game.DrawPile.Count +
            game.DiscardPile.Count;

        Assert.Equal(108, totalCards);
    }

    [Fact]
    public void CreateGame_AssignsUniquePlayerIds()
    {
        var game = UnoGameFactory.CreateGame(
            ["Alice", "Bob", "Charlie"],
            new Random(123));

        var ids = game.Players
            .Select(player => player.Id)
            .ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void CreateGame_TrimsPlayerNames()
    {
        var game = UnoGameFactory.CreateGame(
            [" Alice ", " Bob "],
            new Random(123));

        Assert.Equal("Alice", game.Players[0].Name);
        Assert.Equal("Bob", game.Players[1].Name);
    }
}