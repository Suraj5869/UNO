using Xunit;

namespace Uno.GameEngine.Tests;

public sealed class UnoGameEngineP0Tests
{
    private static UnoGameState Game() => UnoGameFactory.CreateGame(
        ["A", "B", "C", "D"], new Random(42), UnoRuleSet.House);

    private static string[] Cards(UnoGameState g) => g.DrawPile.Concat(g.DiscardPile)
        .Concat(g.Players.SelectMany(p => p.Hand))
        .Select(c => $"{c.Color}/{c.Type}/{c.Number}").Order().ToArray();

    [Theory]
    [InlineData(0, -1, 3)]
    [InlineData(3, 1, 0)]
    [InlineData(2, -1, 1)]
    [InlineData(1, 1, 2)]
    public void CurrentDeparture_RespectsDirection(int index, int direction, int next)
    {
        var g = Game();
        g.CurrentPlayerIndex = index;
        g.TurnDirection = direction;
        var expected = g.Players[next].Id;
        var leaving = g.CurrentPlayer;
        var cards = Cards(g);
        Assert.True(new UnoGameEngine().RemovePlayer(g, leaving.Id));
        Assert.Equal(expected, g.CurrentPlayer.Id);
        Assert.Empty(leaving.Hand);
        Assert.Equal(cards, Cards(g));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void BystanderDeparture_PreservesTurn(int index)
    {
        var g = Game();
        g.CurrentPlayerIndex = 1;
        var actor = g.CurrentPlayer;
        g.JustDrawnCard = actor.Hand[0];
        var deadline = g.TurnDeadlineUtc;
        var drawn = g.JustDrawnCard;
        Assert.True(new UnoGameEngine().RemovePlayer(g, g.Players[index].Id));
        Assert.Equal(actor.Id, g.CurrentPlayer.Id);
        Assert.Equal(deadline, g.TurnDeadlineUtc);
        Assert.Same(drawn, g.JustDrawnCard);
    }

    [Fact]
    public void PenalizedDeparture_TransfersPenaltyInReverse()
    {
        var g = Game();
        g.CurrentPlayerIndex = 2;
        g.TurnDirection = -1;
        g.PendingDrawType = CardType.DrawTwo;
        g.PendingDrawAmount = 4;
        var next = g.Players[1];
        var engine = new UnoGameEngine();
        engine.RemovePlayer(g, g.CurrentPlayer.Id);
        Assert.Equal(next.Id, g.CurrentPlayer.Id);
        Assert.Equal(4, g.PendingDrawAmount);
        engine.AcceptDrawPenalty(g, next.Id);
        Assert.Equal(11, next.Hand.Count);
        Assert.Equal(0, g.PendingDrawAmount);
    }

    [Theory]
    [InlineData(UnoRuleSet.Official)]
    [InlineData(UnoRuleSet.House)]
    public void OffenderDeparture_PreservesPenaltyWithoutChallenge(UnoRuleSet rules)
    {
        var g = UnoGameFactory.CreateGame(["A", "B", "C"], new Random(42), rules);
        g.CurrentPlayerIndex = 1;
        g.PendingDrawType = CardType.WildDrawFour;
        g.PendingDrawAmount = 4;
        g.WildDrawFourOffenderIndex = 0;
        g.WildDrawFourWasIllegal = true;
        var actor = g.CurrentPlayer;
        var deadline = g.TurnDeadlineUtc;
        var engine = new UnoGameEngine();
        engine.RemovePlayer(g, g.Players[0].Id);
        Assert.False(g.IsWildDrawFourChallengePending);
        Assert.Equal(deadline, g.TurnDeadlineUtc);
        Assert.Equal(actor.Id, g.CurrentPlayer.Id);
        engine.AcceptDrawPenalty(g, actor.Id);
        Assert.Equal(11, actor.Hand.Count);
    }

    [Fact]
    public void ConsecutiveDepartures_CompleteAndConserveCards()
    {
        var g = Game();
        var engine = new UnoGameEngine();
        var cards = Cards(g);
        g.PendingDrawType = CardType.DrawTwo;
        g.PendingDrawAmount = 2;
        while (g.Players.Count > 1)
            Assert.True(engine.RemovePlayer(g, g.CurrentPlayer.Id));
        Assert.Equal(GameStatus.Completed, g.Status);
        Assert.Equal(cards, Cards(g));
        Assert.Equal(0, g.PendingDrawAmount);
    }

    [Theory]
    [InlineData("play")]
    [InlineData("draw")]
    [InlineData("pass")]
    [InlineData("accept")]
    [InlineData("wildAccept")]
    [InlineData("challenge")]
    public void ExpiredTurn_RejectsActionWithoutMutation(string action)
    {
        var g = Game();
        var actor = g.CurrentPlayer;
        var engine = new UnoGameEngine();
        g.TurnDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var count = actor.Hand.Count;
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            switch (action)
            {
                case "play": engine.PlayCard(g, actor.Id, actor.Hand[0]); break;
                case "draw": engine.DrawCard(g, actor.Id); break;
                case "pass": engine.PassAfterDraw(g, actor.Id); break;
                case "accept": engine.AcceptDrawPenalty(g, actor.Id); break;
                case "wildAccept": engine.AcceptWildDrawFour(g, actor.Id); break;
                case "challenge": engine.ChallengeWildDrawFour(g, actor.Id); break;
            }
        });
        Assert.Contains("deadline", error.Message);
        Assert.Equal(count, actor.Hand.Count);
        Assert.Equal(actor.Id, g.CurrentPlayer.Id);
        var tick = DateTimeOffset.UtcNow;
        Assert.True(engine.AutoResolveTimedOutTurn(g, tick));
        Assert.False(engine.AutoResolveTimedOutTurn(g, tick));
        Assert.Equal(count + 1, actor.Hand.Count);
    }

    [Fact]
    public void TimeoutAfterDrawing_DoesNotDrawAgain()
    {
        var g = Game();
        var actor = g.CurrentPlayer;
        g.JustDrawnCard = actor.Hand[0];
        var count = actor.Hand.Count;
        var engine = new UnoGameEngine();
        Assert.True(engine.AutoResolveTimedOutTurn(g, g.TurnDeadlineUtc));
        Assert.Equal(count, actor.Hand.Count);
        Assert.False(g.CanPassAfterDraw);
    }
}
