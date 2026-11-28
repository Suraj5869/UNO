using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Uno.Api.Controllers;
using Uno.Api.Progression;
using Uno.Api.Rooms;

public sealed class MatchPersistenceTests
{
    [Fact]
    public async Task MatchPersistenceIsAtomicIdempotentAndPrivate()
    {
        var connectionString = Environment.GetEnvironmentVariable("UNO_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (Environment.GetEnvironmentVariable("CI") == "true")
                throw new InvalidOperationException("UNO_TEST_POSTGRES is required in CI.");
            return;
        }
        await using var source = NpgsqlDataSource.Create(connectionString);
        await using var db = await source.OpenConnectionAsync();
        var user = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        await db.ExecuteAsync("""
            insert into uno.users(id,email,password_hash) values (@User,@Email,'test');
            insert into uno.player_progress(user_id) values (@User);
            insert into uno.player_statistics(user_id) values (@User);
            """, new { User = user, Email = user + "@example.test" });
        var rooms = new RoomStore();
        using var services = new ServiceCollection().AddSingleton(source).AddSingleton(rooms).BuildServiceProvider();
        var writer = new MatchResultWriter(rooms, services, NullLogger<MatchResultWriter>.Instance, new ConfigurationBuilder().Build());
        var now = DateTimeOffset.UtcNow;
        var match = new CompletedMatch(Guid.NewGuid(), Guid.NewGuid(), "TEST01", 2, 0,
            now.AddMinutes(-1), now, 12, [new ResultPlayer(Guid.NewGuid(), user, "Player", true)], "bots", 0);
        var controller = new ProgressionController(services);
        void SignIn(Guid id) => controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "test")) }
        };
        SignIn(user);
        Assert.IsType<NotFoundResult>(await controller.Reward(match.Id, default));
        var checkpoint = System.Text.Json.JsonSerializer.Deserialize<RoomStore.RuntimeCheckpoint>(rooms.ExportRuntime())!;
        rooms.RestoreRuntime(System.Text.Json.JsonSerializer.Serialize(checkpoint with { Pending = [match] }));
        Assert.IsType<AcceptedObjectResult>(await controller.Reward(match.Id, default));
        SignIn(outsider);
        Assert.IsType<NotFoundResult>(await controller.Reward(match.Id, default));
        SignIn(user);
        var recovered = new RoomStore();
        recovered.RestoreRuntime(rooms.ExportRuntime());
        Assert.Equal(match.Id, Assert.Single(recovered.PendingResults()).Id);
        // Force a foreign-key failure after the match insert; the entire transaction must roll back.
        var invalid = match with { Players = [new ResultPlayer(Guid.NewGuid(), outsider, "Missing", true)] };
        await Assert.ThrowsAsync<PostgresException>(() => writer.Save(source, invalid, default));
        Assert.Equal(0L, await db.ExecuteScalarAsync<long>("select count(*) from uno.matches where id=@Id", new { match.Id }));
        Assert.IsType<AcceptedObjectResult>(await controller.Reward(match.Id, default));
        await writer.Save(source, match, default);
        rooms.AcknowledgeResult(match.Id);
        Assert.Empty(rooms.PendingResults());
        await writer.Save(source, match, default);
        Assert.Equal(1L, await db.ExecuteScalarAsync<long>("select count(*) from uno.xp_transactions where match_id=@Id", new { match.Id }));
        Assert.Equal(10L, await db.ExecuteScalarAsync<long>("select total_xp from uno.player_progress where user_id=@User", new { User = user }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("select matches_played from uno.player_statistics where user_id=@User", new { User = user }));
        var reward = Assert.IsType<ProgressionController.HistoryRow>(Assert.IsType<OkObjectResult>(await controller.Reward(match.Id, default)).Value);
        Assert.Equal(10L, reward.Xp);
        SignIn(outsider);
        Assert.IsType<NotFoundResult>(await controller.Reward(match.Id, default));
        SignIn(user);
        var zero = match with { Id = Guid.NewGuid(), CardsPlayed = 0 };
        await writer.Save(source, zero, default);
        var zeroReward = Assert.IsType<ProgressionController.HistoryRow>(Assert.IsType<OkObjectResult>(await controller.Reward(zero.Id, default)).Value);
        Assert.Equal(0L, zeroReward.Xp);
    }
}
