namespace Uno.GameEngine.Tests;

public sealed class UnoGameRoomTests
{
    [Fact]
    public void CreateRoom_AddsCreatorAndDefaultsToOfficialRules()
    {
        var room = new UnoGameRoom("Alice");

        Assert.Equal(UnoRoomStatus.Waiting, room.Status);
        Assert.Equal(UnoRuleSet.Official, room.RuleSet);
        Assert.Single(room.Players);
        Assert.Equal(room.CreatorPlayerId, room.Players[0].Id);
        Assert.True(room.Players[0].IsCreator);
        Assert.Equal(6, room.Code.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void CreateRoom_WithInvalidCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UnoGameRoom("Alice", capacity));
    }

    [Fact]
    public void Join_AddsPlayerWhileRoomIsWaiting()
    {
        var room = new UnoGameRoom("Alice", maximumPlayers: 3);

        var bob = room.Join(" Bob ");

        Assert.Equal("Bob", bob.DisplayName);
        Assert.Equal(2, room.Players.Count);
        Assert.False(bob.IsCreator);
    }

    [Fact]
    public void Join_WhenRoomIsFull_Throws()
    {
        var room = new UnoGameRoom("Alice", maximumPlayers: 2);
        room.Join("Bob");

        Assert.Throws<InvalidOperationException>(() => room.Join("Charlie"));
    }

    [Fact]
    public void SetRuleSet_CreatorCanChooseHouseRulesBeforeStart()
    {
        var room = new UnoGameRoom("Alice");

        room.SetRuleSet(room.CreatorPlayerId, UnoRuleSet.House);

        Assert.Equal(UnoRuleSet.House, room.RuleSet);
    }

    [Fact]
    public void SetRuleSet_NonCreatorCannotChangeRules()
    {
        var room = new UnoGameRoom("Alice");
        var bob = room.Join("Bob");

        Assert.Throws<UnauthorizedAccessException>(
            () => room.SetRuleSet(bob.Id, UnoRuleSet.House));
        Assert.Equal(UnoRuleSet.Official, room.RuleSet);
    }

    [Fact]
    public void StartGame_RequiresAtLeastTwoPlayers()
    {
        var room = new UnoGameRoom("Alice");

        Assert.Throws<InvalidOperationException>(
            () => room.StartGame(room.CreatorPlayerId));
        Assert.Equal(UnoRoomStatus.Waiting, room.Status);
    }

    [Fact]
    public void StartGame_CreatorStartsGameAndMapsRoomPlayersToEnginePlayers()
    {
        var room = new UnoGameRoom("Alice", maximumPlayers: 3);
        var bob = room.Join("Bob");
        room.SetRuleSet(room.CreatorPlayerId, UnoRuleSet.House);

        var game = room.StartGame(room.CreatorPlayerId, new Random(123));

        Assert.Equal(UnoRoomStatus.InProgress, room.Status);
        Assert.Same(game, room.Game);
        Assert.Equal(UnoRuleSet.House, game.RuleSet);
        Assert.Equal(room.Players.Count, game.Players.Count);
        Assert.Equal(game.Players[0].Id, room.Players[0].GamePlayerId);
        Assert.Equal(game.Players[1].Id, bob.GamePlayerId);
    }

    [Fact]
    public void StartGame_NonCreatorCannotStart()
    {
        var room = new UnoGameRoom("Alice");
        var bob = room.Join("Bob");

        Assert.Throws<UnauthorizedAccessException>(
            () => room.StartGame(bob.Id));
        Assert.Equal(UnoRoomStatus.Waiting, room.Status);
    }

    [Fact]
    public void StartGame_LocksMembershipAndRuleSelection()
    {
        var room = new UnoGameRoom("Alice");
        room.Join("Bob");
        room.StartGame(room.CreatorPlayerId, new Random(123));

        Assert.Throws<InvalidOperationException>(() => room.Join("Charlie"));
        Assert.Throws<InvalidOperationException>(
            () => room.SetRuleSet(room.CreatorPlayerId, UnoRuleSet.House));
        Assert.Throws<InvalidOperationException>(
            () => room.StartGame(room.CreatorPlayerId));
    }

    [Fact]
    public void PrepareRematch_ResetsRoomAndPromotesOptedInPlayerToHost()
    {
        var room = new UnoGameRoom("Alice", maximumPlayers: 3);
        var bob = room.Join("Bob");
        room.Join("Charlie");
        room.StartGame(room.CreatorPlayerId, new Random(123));
        room.Game!.Status = GameStatus.Completed;

        room.PrepareRematch(bob.Id);

        Assert.Equal(UnoRoomStatus.Waiting, room.Status);
        Assert.Null(room.Game);
        Assert.Single(room.Players);
        Assert.Equal(bob.Id, room.CreatorPlayerId);
        Assert.True(room.Players[0].IsCreator);
        Assert.Equal(bob.Id, room.Players[0].Id);
        Assert.Null(room.Players[0].GamePlayerId);
    }

    [Fact]
    public void JoinRematch_AddsPreviousPlayerWithoutReplacingNewHost()
    {
        var room = new UnoGameRoom("Alice", maximumPlayers: 3);
        var alice = room.Players[0];
        var bob = room.Join("Bob");
        room.StartGame(room.CreatorPlayerId, new Random(123));
        room.Game!.Status = GameStatus.Completed;
        room.PrepareRematch(bob.Id);

        var returningAlice = room.JoinRematch(alice.Id, alice.DisplayName);
        // The opted-in player remains the host when another player joins.
        Assert.True(room.Players[0].IsCreator);
        Assert.False(returningAlice.IsCreator);
        Assert.Equal(2, room.Players.Count);
    }

    [Fact]
    public void PrepareRematch_WhenGameIsNotCompleted_Throws()
    {
        var room = new UnoGameRoom("Alice");
        room.Join("Bob");
        room.StartGame(room.CreatorPlayerId, new Random(123));

        Assert.Throws<InvalidOperationException>(() => room.PrepareRematch(room.CreatorPlayerId));
    }

    [Fact]
    public void CreateRoom_WithHouseRules_PreservesSelectedPreset()
    {
        var room = new UnoGameRoom("Alice", ruleSet: UnoRuleSet.House);

        Assert.Equal(UnoRuleSet.House, room.RuleSet);
    }
}
