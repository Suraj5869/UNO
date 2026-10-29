namespace Uno.GameEngine;

public sealed record UnoCard
{
    public UnoCard(
        CardColor color,
        CardType type,
        int? number = null)
    {
        var isWild = type is CardType.Wild
            or CardType.WildDrawFour;

        if (isWild && color != CardColor.None)
        {
            throw new ArgumentException(
                "Wild cards must have no fixed color.",
                nameof(color));
        }

        if (!isWild && color == CardColor.None)
        {
            throw new ArgumentException(
                "Non-wild cards must have a color.",
                nameof(color));
        }

        if (type == CardType.Number)
        {
            if (number is null or < 0 or > 9)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(number),
                    "Number cards must have a value from 0 to 9.");
            }
        }
        else if (number is not null)
        {
            throw new ArgumentException(
                "Only number cards can have a number value.",
                nameof(number));
        }

        Color = color;
        Type = type;
        Number = number;
    }

    public CardColor Color { get; }

    public CardType Type { get; }

    public int? Number { get; }
}