namespace Uno.GameEngine;

/// <summary>Lifecycle state of a private multiplayer room.</summary>
public enum UnoRoomStatus
{
    Waiting = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}
