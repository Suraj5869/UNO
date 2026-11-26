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
}
