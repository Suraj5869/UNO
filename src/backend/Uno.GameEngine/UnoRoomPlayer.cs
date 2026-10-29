namespace Uno.GameEngine;

/// <summary>
/// A participant in a private room. GamePlayerId maps this room participant
/// to the engine player after the room starts.
/// </summary>
public sealed class UnoRoomPlayer
{
    internal UnoRoomPlayer(Guid id, string displayName, bool isCreator)
    {
        Id = id;
        DisplayName = displayName;
        IsCreator = isCreator;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public bool IsCreator { get; }

    public Guid? GamePlayerId { get; internal set; }
}
