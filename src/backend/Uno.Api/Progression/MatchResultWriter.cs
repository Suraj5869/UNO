using Dapper;
using Npgsql;
using Uno.Api.Rooms;

namespace Uno.Api.Progression;

public sealed record ResultPlayer(Guid PlayerId, Guid? UserId, string DisplayName, bool Won, bool Abandoned = false, bool Participated = true);
public sealed record CompletedMatch(Guid Id, Guid RoomId, string Code, int MaximumPlayers, int RuleSet,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int CardsPlayed, ResultPlayer[] Players, string Mode = "rooms", int? Difficulty = null, bool Abandoned = false);

public sealed class MatchResultWriter(RoomStore rooms, IServiceProvider services, ILogger<MatchResultWriter> logger, IConfiguration configuration) : BackgroundService
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

    public async Task Save(NpgsqlDataSource source, CompletedMatch match, CancellationToken ct)
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
            insert into uno.matches(id,room_id,variant,rule_set,status,started_at,completed_at,match_mode,bot_difficulty)
            values (@Id,@RoomId,'classic',@RuleSet,'completed',@StartedAt,@CompletedAt,@Mode,@Difficulty)
            on conflict(id) do nothing
            """, match);
        if (inserted == 0) { await tx.CommitAsync(ct); return; }
        foreach (var player in match.Players.OrderBy(p => p.UserId))
        {
            await Run("""
                insert into uno.match_players(match_id,player_id,user_id,display_name,finish_position,abandoned)
                values (@MatchId,@PlayerId,@UserId,@DisplayName,@Position,@Abandoned)
                """, new { MatchId=match.Id, player.PlayerId, player.UserId, player.DisplayName, Position=player.Won ? (int?)1 : null, Abandoned=match.Abandoned || player.Abandoned });
            if (player.UserId is not Guid userId) continue;
            var xp = match.Abandoned || player.Abandoned || !player.Participated || match.CardsPlayed < 7 ? 0 : match.Mode == "bots" ? (5 + 5 * Math.Clamp(match.Difficulty ?? 0, 0, 2)) * (player.Won ? 2 : 1) : ProgressionPolicy.Award(player.Won, true, match.CardsPlayed);
            await Run("""
                update uno.player_statistics set matches_played=matches_played+1,
                wins=wins+@Win where user_id=@UserId
                """, new { UserId=userId, Win=player.Won ? 1 : 0 });
            if (xp == 0) continue;
            await Run("update uno.player_progress set total_xp=total_xp where user_id=@UserId", new { UserId=userId });
            if (match.Mode is "bots" or "rooms")
            {
                var cap = Math.Max(0, configuration.GetValue<int?>("Progression:" + (match.Mode=="bots" ? "BotDailyXp" : "RoomDailyXp")) ?? (match.Mode=="bots" ? 300 : 500));
                var day = new DateTimeOffset(match.CompletedAt.UtcDateTime.Date, TimeSpan.Zero);
                var earned = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
                    select coalesce(sum(x.amount),0) from uno.xp_transactions x join uno.matches m on m.id=x.match_id
                    where x.user_id=@UserId and m.match_mode=@Mode and m.completed_at>=@Start and m.completed_at<@End
                    """, new { UserId=userId, match.Mode, Start=day, End=day.AddDays(1) }, tx, cancellationToken:ct));
                xp = (int)Math.Min(xp, Math.Max(0L, cap-earned));
                if (xp == 0) continue;
            }
            await Run("""
                insert into uno.xp_transactions(user_id,match_id,reason,amount,idempotency_key)
                values (@UserId,@MatchId,'match_completed',@Xp,@Key)
                """, new { UserId=userId, MatchId=match.Id, Xp=xp, Key=$"match:{match.Id:N}:{userId:N}" });
            var total = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
                update uno.player_progress set total_xp=total_xp+@Xp where user_id=@UserId returning total_xp
                """, new { UserId=userId, Xp=xp }, tx, cancellationToken:ct));
            await Run("update uno.player_progress set level=@Level where user_id=@UserId",
                new { UserId=userId, Level=ProgressionPolicy.Level(total) });
            var start = new DateTimeOffset(match.CompletedAt.Year,match.CompletedAt.Month,1,0,0,0,TimeSpan.Zero);
            await Run("""
                insert into uno.player_inventory(user_id,item_id)
                select @UserId,c.id from uno.cosmetic_items c
                where c.is_active and c.required_level<=@Level and c.required_season_xp<=(
                    select coalesce(sum(x.amount),0) from uno.xp_transactions x
                    join uno.matches m on m.id=x.match_id
                    where x.user_id=@UserId and m.match_mode='ranked' and m.completed_at>=@Start and m.completed_at<@End)
                on conflict do nothing
                """, new { UserId=userId, Level=ProgressionPolicy.Level(total), Start=start, End=start.AddMonths(1) });
        }
        await tx.CommitAsync(ct);
    }
}
