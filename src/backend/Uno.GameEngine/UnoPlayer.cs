namespace Uno.GameEngine;

public sealed class UnoPlayer
{
    private readonly List<UnoCard> _hand = [];

    internal UnoPlayer(string name)
    {
        Id = Guid.NewGuid();
        Name = name;
    }

    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<UnoCard> Hand => _hand.AsReadOnly();

    internal void AddCard(UnoCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _hand.Add(card);
    }

    internal bool RemoveCard(UnoCard card)
    {
        return _hand.Remove(card);
    }
}