using Uno.GameEngine;

UnoApi.Build(args, services =>
{
    services.AddHttpContextAccessor();
    services.AddSingleton<Func<UnoGameRoom, Random?>>(provider => room =>
    {
        var request = provider.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request;
        var fixture = request?.Headers["X-Test-Deal"].ToString();
        if (string.IsNullOrEmpty(fixture)) return null;
        if (fixture == "numbers") return new OrderedDeck();

        var names = room.Players.Select(player => player.DisplayName).ToArray();
        for (var seed = 0; seed < 10000; seed++)
        {
            var game = UnoGameFactory.CreateGame(names, new Random(seed), room.RuleSet);
            var hand = game.CurrentPlayer.Hand;
            bool Has(CardType type) => hand.Any(card => card.Type == type && card.Color == game.ActiveColor);
            var matches = fixture switch
            {
                "skip" => Has(CardType.Skip),
                "reverse" => Has(CardType.Reverse),
                "drawTwo" => Has(CardType.DrawTwo) && game.Players[1].Hand.Any(c => c.Type == CardType.DrawTwo),
                "wild" => hand.Any(c => c.Type == CardType.Wild),
                "illegalFour" or "legalFour" => hand.Any(c => c.Type == CardType.WildDrawFour)
                    && hand.Any(c => c.Color == game.ActiveColor) == (fixture == "illegalFour"),
                _ => throw new ArgumentException("Unknown browser fixture.")
            };
            if (matches) return new Random(seed);
        }
        throw new InvalidOperationException("Could not find the requested opening deal.");
    });
}).Run();

// Leave the standard deck in factory order during Fisher-Yates.
sealed class OrderedDeck : Random
{
    public override int Next(int maxValue) => maxValue - 1;
}
