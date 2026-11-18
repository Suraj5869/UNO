using Xunit;

namespace Uno.GameEngine.Tests;

public sealed class UnoGameEngineP1Tests
{
    public static IEnumerable<object[]> OpeningActions() =>
        from rules in Enum.GetValues<UnoRuleSet>()
        from type in new[] { CardType.Skip, CardType.Reverse, CardType.DrawTwo, CardType.Wild, CardType.WildDrawFour }
        select new object[] { rules, type };

    [Theory]
    [MemberData(nameof(OpeningActions))]
    public void FirstTurn_AllowsMatchingAction(UnoRuleSet rules, CardType type)
    {
        var game = UnoGameFactory.CreateGame(["A", "B", "C", "D"], new Random(42), rules);
        var player = game.CurrentPlayer;
        var wild = type is CardType.Wild or CardType.WildDrawFour;
        var card = new UnoCard(wild ? CardColor.None : game.ActiveColor, type, null);
        player.AddCard(card);
        var before = player.Hand.Count;
        Assert.Equal(CardType.Number, game.TopDiscard.Type);
        new UnoGameEngine().PlayCard(game, player.Id, card, wild ? CardColor.Blue : CardColor.None);
        Assert.Equal(card, game.TopDiscard);
        Assert.Equal(before - 1, player.Hand.Count);
        Assert.Equal(wild ? CardColor.Blue : card.Color, game.ActiveColor);
        Assert.Equal(type == CardType.Reverse ? -1 : 1, game.TurnDirection);
        Assert.Equal(type == CardType.Reverse ? 3 : type == CardType.Skip ||
            (type == CardType.DrawTwo && rules == UnoRuleSet.Official) ? 2 : 1, game.CurrentPlayerIndex);
        Assert.Equal(type == CardType.WildDrawFour ? 4 :
            type == CardType.DrawTwo && rules == UnoRuleSet.House ? 2 : 0, game.PendingDrawAmount);
        Assert.Equal(type == CardType.DrawTwo && rules == UnoRuleSet.Official ? 9 : 7, game.Players[1].Hand.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Challenge_ReportsOnlyAvailableCards(bool successful)
    {
        var game = UnoGameFactory.CreateGame(["A", "B", "C"], new Random(42));
        game.CurrentPlayerIndex = 1;
        game.PendingDrawAmount = 4;
        game.PendingDrawType = CardType.WildDrawFour;
        game.WildDrawFourOffenderIndex = 0;
        game.WildDrawFourWasIllegal = successful;
        game.MutableDrawPile.RemoveRange(1, game.DrawPile.Count - 1);
        var target = game.Players[successful ? 0 : 1];
        var before = target.Hand.Count;
        var result = new UnoGameEngine().ChallengeWildDrawFour(game, game.CurrentPlayer.Id);
        Assert.Equal(target.Id, result.DrawnPlayerId);
        Assert.Equal(1, result.DrawCount);
        Assert.Equal(before + 1, target.Hand.Count);
    }
}
