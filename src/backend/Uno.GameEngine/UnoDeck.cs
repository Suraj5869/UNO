namespace Uno.GameEngine;

public static class UnoDeck
{
    public static IReadOnlyList<UnoCard> CreateStandardDeck()
    {
        var cards = new List<UnoCard>(108);

        var colors = new[]
        {
            CardColor.Red,
            CardColor.Yellow,
            CardColor.Green,
            CardColor.Blue
        };

        foreach (var color in colors)
        {
            // One zero card per color.
            cards.Add(new UnoCard(
                color,
                CardType.Number,
                0));

            // Two copies of every number from 1 to 9.
            for (var number = 1; number <= 9; number++)
            {
                cards.Add(new UnoCard(
                    color,
                    CardType.Number,
                    number));

                cards.Add(new UnoCard(
                    color,
                    CardType.Number,
                    number));
            }

            // Two copies of each colored action card.
            for (var copy = 0; copy < 2; copy++)
            {
                cards.Add(new UnoCard(
                    color,
                    CardType.Skip));

                cards.Add(new UnoCard(
                    color,
                    CardType.Reverse));

                cards.Add(new UnoCard(
                    color,
                    CardType.DrawTwo));
            }
        }

        // Four Wild cards and four Wild Draw Four cards.
        for (var copy = 0; copy < 4; copy++)
        {
            cards.Add(new UnoCard(
                CardColor.None,
                CardType.Wild));

            cards.Add(new UnoCard(
                CardColor.None,
                CardType.WildDrawFour));
        }

        return cards.AsReadOnly();
    }

    public static List<UnoCard> CreateShuffledDeck(
        Random? random = null)
    {
        var cards = CreateStandardDeck().ToList();
        random ??= Random.Shared;

        // Fisher-Yates shuffle.
        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        return cards;
    }
}