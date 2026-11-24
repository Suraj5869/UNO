using Dapper;
using Npgsql;
using Uno.Api.Rooms;

namespace Uno.Api.Progression;

public sealed record ResultPlayer(Guid PlayerId, Guid? UserId, string DisplayName, bool Won);
public sealed record CompletedMatch(Guid Id, Guid RoomId, string Code, int MaximumPlayers, int RuleSet,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int CardsPlayed, ResultPlayer[] Players);

public sealed class MatchResultWriter(RoomStore rooms, IServiceProvider services, ILogger<MatchResultWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (services.GetService<NpgsqlDataSource>() is not {} source) continue;
            foreach (var match in rooms.PendingResults())
            {
                try { await Save(source, match, stoppingToken); rooms.AcknowledgeResult(match.Id); }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { logger.LogWarning(ex, "Match result {MatchId} will be retried", match.Id); }
            }
        }
    }

    private static async Task Save(NpgsqlDataSource source, CompletedMatch match, CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        Task<int> Run(string sql, object args) => db.ExecuteAsync(new CommandDefinition(sql, args, tx, cancellationToken: ct));
        await Run("""
            insert into uno.rooms(id,code,rule_set,maximum_players,status,closed_at)
            values (@RoomId,@Code,@RuleSet,@MaximumPlayers,'completed',@CompletedAt)
            on conflict(id) do nothing
            """, match);
        var inserted = await Run("""
            insert into uno.matches(id,room_id,variant,rule_set,status,started_at,completed_at)
            values (@Id,@RoomId,'classic',@RuleSet,'completed',@StartedAt,@CompletedAt)
            on conflict(id) do nothing
            """, match);
        if (inserted == 0) { await tx.CommitAsync(ct); return; }
        foreach (var player in match.Players.OrderBy(p => p.UserId))
        {
            await Run("""
                insert into uno.match_players(match_id,player_id,user_id,display_name,finish_position)
                values (@MatchId,@PlayerId,@UserId,@DisplayName,@Position)
                """, new { MatchId=match.Id, player.PlayerId, player.UserId, player.DisplayName, Position=player.Won ? (int?)1 : null });
            if (player.UserId is not Guid userId) continue;
            var xp = ProgressionPolicy.Award(player.Won, true, match.CardsPlayed);
            await Run("""
                update uno.player_statistics set matches_played=matches_played+1,
                wins=wins+@Win where user_id=@UserId
                """, new { UserId=userId, Win=player.Won ? 1 : 0 });
            if (xp == 0) continue;
            await Run("""
                insert into uno.xp_transactions(user_id,match_id,reason,amount,idempotency_key)
                values (@UserId,@MatchId,'match_completed',@Xp,@Key)
                """, new { UserId=userId, MatchId=match.Id, Xp=xp, Key=$"match:{match.Id:N}:{userId:N}" });
            var total = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
                update uno.player_progress set total_xp=total_xp+@Xp where user_id=@UserId returning total_xp
                """, new { UserId=userId, Xp=xp }, tx, cancellationToken:ct));
            await Run("update uno.player_progress set level=@Level where user_id=@UserId",
                new { UserId=userId, Level=ProgressionPolicy.Level(total) });
        }
        await tx.CommitAsync(ct);
    }
}
