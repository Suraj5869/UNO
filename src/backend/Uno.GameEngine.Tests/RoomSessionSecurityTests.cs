using Uno.GameEngine;
using Uno.Api.Rooms;

public sealed class RoomSessionSecurityTests
{
    [Fact]
    public void AccountRoomRequiresOriginalSession()
    {
        var store = new RoomStore();
        var account = Guid.NewGuid();
        var session = Guid.NewGuid();
        var room = store.Create(new CreateRoomRequest("Alice"), account);
        store.BindSession(room.PlayerId, session);
        store.RequireSession(room.Room.Code, room.AccessToken, account, session);
        Assert.Throws<UnauthorizedAccessException>(() => store.RequireSession(room.Room.Code, room.AccessToken, account, Guid.NewGuid()));
        Assert.Throws<UnauthorizedAccessException>(() => store.RequireSession(room.Room.Code, room.AccessToken, null, null));
        Assert.Throws<UnauthorizedAccessException>(() => store.RequireSession(room.Room.Code, room.AccessToken, Guid.NewGuid(), session));
    }

    [Fact]
    public void RevocationDoesNotRemoveOtherSessionSeats()
    {
        var store = new RoomStore();
        var account = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var a = store.Create(new CreateRoomRequest("Alice"), account);
        var b = store.Create(new CreateRoomRequest("Alice"), account);
        store.BindSession(a.PlayerId, first);
        store.BindSession(b.PlayerId, second);
        store.RevokeSessionRooms(account, first);
        Assert.Throws<UnauthorizedAccessException>(() => store.RequireSession(a.Room.Code, a.AccessToken, account, first));
        store.RequireSession(b.Room.Code, b.AccessToken, account, second);
    }

    [Fact]
    public void GuestRoomDoesNotRequireAccount()
    {
        var store = new RoomStore();
        var room = store.Create(new CreateRoomRequest("Guest"));
        store.BindSession(room.PlayerId, null);
        store.RequireSession(room.Room.Code, room.AccessToken, null, null);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Current = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    [Fact]
    public void ExpirySurvivesCheckpointAndRejectsConnectedSeat()
    {
        var clock = new ManualClock();
        var store = new RoomStore(clock: clock);
        var a = store.Create(new CreateRoomRequest("Alice"));
        store.Connected(a.Room.Code, a.AccessToken, "connection");
        clock.Current = clock.Current.AddHours(23);
        var restored = new RoomStore(clock: clock);
        restored.RestoreRuntime(store.ExportRuntime());
        restored.Connected(a.Room.Code, a.AccessToken, "restored");
        clock.Current = clock.Current.AddHours(1);
        Assert.Throws<UnauthorizedAccessException>(() => restored.AuthorizeParticipant(a.Room.Code, a.AccessToken));
        Assert.Throws<UnauthorizedAccessException>(() => restored.RequireSession(a.Room.Code, a.AccessToken, null, null));
        Assert.Empty(restored.AuthorizedConnections(a.Room.Code));
        Assert.Contains(a.Room.Code, restored.ExpireDisconnected());
    }

    [Fact]
    public void RematchRotatesCredentialAndInvalidatesPreviousConnection()
    {
        var store = new RoomStore();
        var a = store.Create(new CreateRoomRequest("Alice"));
        store.Join(a.Room.Code, "Bob");
        store.StartGame(a.Room.Code, a.AccessToken);
        a.Room.Game!.Status = GameStatus.Completed;
        store.Connected(a.Room.Code, a.AccessToken, "old");
        var next = store.PlayAgain(a.Room.Code, a.AccessToken);
        Assert.NotEqual(a.AccessToken, next.AccessToken);
        Assert.Equal(a.PlayerId, store.AuthorizeParticipant(a.Room.Code, next.AccessToken));
        Assert.Throws<UnauthorizedAccessException>(() => store.AuthorizeParticipant(a.Room.Code, a.AccessToken));
        Assert.Empty(store.AuthorizedConnections(a.Room.Code));
    }

    [Fact]
    public void RevisionRejectsStaleStateAfterTimeoutAndAcrossRooms()
    {
        var store = new RoomStore();
        var a = store.Create(new CreateRoomRequest("Alice"));
        store.Join(a.Room.Code, "Bob");
        var game = store.StartGame(a.Room.Code, a.AccessToken);
        var revision = GameStateResponse.Revision(a.Room);
        GameStateResponse.RequireRevision(a.Room, revision);
        Assert.True(new UnoGameEngine().AutoResolveTimedOutTurn(game, game.TurnDeadlineUtc.AddSeconds(1)));
        Assert.Throws<InvalidOperationException>(() => GameStateResponse.RequireRevision(a.Room, revision));
        var next = GameStateResponse.Revision(a.Room);
        GameStateResponse.RequireRevision(a.Room, next);
        var b = store.Create(new CreateRoomRequest("Carol"));
        store.Join(b.Room.Code, "Dan");
        store.StartGame(b.Room.Code, b.AccessToken);
        Assert.Throws<InvalidOperationException>(() => GameStateResponse.RequireRevision(b.Room, next));
    }
}
