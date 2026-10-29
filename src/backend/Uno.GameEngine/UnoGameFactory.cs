namespace Uno.GameEngine;

public static class UnoGameFactory
{
    public const int MinimumPlayers = 2;
    public const int MaximumPlayers = 10;
    public const int StartingHandSize = 7;

    public static UnoGameState CreateGame(
        IReadOnlyList<string> playerNames,
        Random? random = null,
        UnoRuleSet ruleSet = UnoRuleSet.Official)
    {
        ArgumentNullException.ThrowIfNull(playerNames);

        if (!Enum.IsDefined(ruleSet))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ruleSet),
                "Select either Official or House rules.");
        }

        if (playerNames.Count < MinimumPlayers ||
            playerNames.Count > MaximumPlayers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerNames),
                $"A game requires {MinimumPlayers} to " +
                $"{MaximumPlayers} players.");
        }

        if (playerNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Player names cannot be empty.",
                nameof(playerNames));
        }

        var players = playerNames
            .Select(name => new UnoPlayer(name.Trim()))
            .ToList();

        var deck = UnoDeck.CreateShuffledDeck(random);

        // Deal seven cards to each player, one card per round.
        for (var round = 0; round < StartingHandSize; round++)
        {
            foreach (var player in players)
            {
                var card = deck[0];
                deck.RemoveAt(0);
                player.AddCard(card);
            }
        }

        // Temporary setup rule: begin with a number card.
        // This avoids unresolved initial action-card behavior.
        var startingCardIndex = deck.FindIndex(
            card => card.Type == CardType.Number);

        if (startingCardIndex < 0)
        {
            throw new InvalidOperationException(
                "The deck contains no number card for the initial discard.");
        }

        var startingCard = deck[startingCardIndex];
        deck.RemoveAt(startingCardIndex);

        var discardPile = new List<UnoCard>
        {
            startingCard
        };

        return new UnoGameState(
            players,
            deck,
            discardPile,
            startingCard.Color,
            currentPlayerIndex: 0,
            turnDirection: 1,
            status: GameStatus.InProgress,
            ruleSet: ruleSet);
    }
}