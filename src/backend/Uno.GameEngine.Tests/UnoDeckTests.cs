using Xunit;

namespace Uno.GameEngine.Tests;

public class UnoDeckTests
{
    [Fact]
    public void CreateStandardDeck_Returns108Cards()
    {
        var deck = UnoDeck.CreateStandardDeck();

        Assert.Equal(108, deck.Count);
    }

    [Fact]
    public void CreateStandardDeck_HasCorrectNumberCardCounts()
    {
        var deck = UnoDeck.CreateStandardDeck();

        foreach (var color in GetColors())
        {
            Assert.Single(
                deck.Where(card =>
                    card.Color == color &&
                    card.Type == CardType.Number &&
                    card.Number == 0));

            for (var number = 1; number <= 9; number++)
            {
                Assert.Equal(
                    2,
                    deck.Count(card =>
                        card.Color == color &&
                        card.Type == CardType.Number &&
                        card.Number == number));
            }
        }

        Assert.Equal(
            76,
            deck.Count(card => card.Type == CardType.Number));
    }

    [Fact]
    public void CreateStandardDeck_HasCorrectActionCardCounts()
    {
        var deck = UnoDeck.CreateStandardDeck();

        foreach (var color in GetColors())
        {
            Assert.Equal(
                2,
                deck.Count(card =>
                    card.Color == color &&
                    card.Type == CardType.Skip));

            Assert.Equal(
                2,
                deck.Count(card =>
                    card.Color == color &&
                    card.Type == CardType.Reverse));

            Assert.Equal(
                2,
                deck.Count(card =>
                    card.Color == color &&
                    card.Type == CardType.DrawTwo));
        }

        Assert.Equal(
            24,
            deck.Count(card =>
                card.Type is CardType.Skip
                    or CardType.Reverse
                    or CardType.DrawTwo));
    }

    [Fact]
    public void CreateStandardDeck_HasFourOfEachWildCard()
    {
        var deck = UnoDeck.CreateStandardDeck();

        Assert.Equal(
            4,
            deck.Count(card => card.Type == CardType.Wild));

        Assert.Equal(
            4,
            deck.Count(card =>
                card.Type == CardType.WildDrawFour));

        Assert.All(
            deck.Where(card =>
                card.Type is CardType.Wild
                    or CardType.WildDrawFour),
            card => Assert.Equal(CardColor.None, card.Color));
    }

    [Fact]
    public void CreateStandardDeck_ContainsAllFourColors()
    {
        var deck = UnoDeck.CreateStandardDeck();

        foreach (var color in GetColors())
        {
            Assert.Contains(
                deck,
                card => card.Color == color);
        }
    }

    [Fact]
    public void UnoCard_RejectsNumberOutsideValidRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnoCard(
                CardColor.Red,
                CardType.Number,
                10));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnoCard(
                CardColor.Blue,
                CardType.Number,
                -1));
    }

    [Fact]
    public void UnoCard_RejectsNumberCardWithoutNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnoCard(
                CardColor.Red,
                CardType.Number));
    }

    [Fact]
    public void UnoCard_RejectsColoredWildCard()
    {
        Assert.Throws<ArgumentException>(
            () => new UnoCard(
                CardColor.Red,
                CardType.Wild));
    }

    [Fact]
    public void UnoCard_RejectsColorlessSkipCard()
    {
        Assert.Throws<ArgumentException>(
            () => new UnoCard(
                CardColor.None,
                CardType.Skip));
    }

    [Fact]
    public void UnoCard_RejectsNumberOnActionCard()
    {
        Assert.Throws<ArgumentException>(
            () => new UnoCard(
                CardColor.Green,
                CardType.Skip,
                5));
    }

    [Fact]
    public void CreateShuffledDeck_PreservesAllCards()
    {
        var original = UnoDeck.CreateStandardDeck()
            .OrderBy(card => card.Color)
            .ThenBy(card => card.Type)
            .ThenBy(card => card.Number)
            .ToArray();

        var shuffled = UnoDeck.CreateShuffledDeck(
                new Random(12345))
            .OrderBy(card => card.Color)
            .ThenBy(card => card.Type)
            .ThenBy(card => card.Number)
            .ToArray();

        Assert.Equal(original, shuffled);
    }

    private static CardColor[] GetColors()
    {
        return
        [
            CardColor.Red,
            CardColor.Yellow,
            CardColor.Green,
            CardColor.Blue
        ];
    }
}