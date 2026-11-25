using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Uno.Api.Progression;

namespace Uno.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/progression")]
public sealed class ProgressionController(IServiceProvider services) : ControllerBase
{
    private sealed record ProgressRow(int Level, long TotalXp, int MatchesPlayed, int Wins);
    public sealed record HistoryRow(Guid MatchId, DateTime CompletedAt, string Variant, int RuleSet, bool Won, long Xp, string Mode, int? Difficulty, bool Abandoned, long SeasonalXp);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var id)) return Unauthorized();
        if (services.GetService<NpgsqlDataSource>() is not {} source) return StatusCode(503);
        await using var db = await source.OpenConnectionAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<ProgressRow>(new CommandDefinition("""
            select p.level, p.total_xp as TotalXp, s.matches_played as MatchesPlayed, s.wins
            from uno.player_progress p join uno.player_statistics s using(user_id) where p.user_id=@Id
            """, new { Id = id }, cancellationToken: ct));
        if (row is null) return NotFound();
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var seasonalXp = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            select coalesce(sum(x.amount),0) from uno.xp_transactions x
            join uno.matches m on m.id=x.match_id
            where x.user_id=@Id and m.match_mode='ranked' and m.completed_at>=@Start and m.completed_at<@End
            """, new { Id = id, Start = start, End = start.AddMonths(1) }, cancellationToken: ct));
        return Ok(new { row.Level, row.TotalXp, row.MatchesPlayed, row.Wins,
            Losses = row.MatchesPlayed - row.Wins,
            WinRate = row.MatchesPlayed == 0 ? 0 : 100.0 * row.Wins / row.MatchesPlayed,
            NextLevelXp = ProgressionPolicy.Threshold(row.Level + 1),
            Season = ProgressionPolicy.Season(now), SeasonalXp = seasonalXp,
            SeasonalLevel = ProgressionPolicy.Level(seasonalXp), SeasonEndsAt = start.AddMonths(1) });
    }

    [HttpGet("reward/{matchId:guid}")]
    public async Task<IActionResult> Reward(Guid matchId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var id)) return Unauthorized();
        if (services.GetService<NpgsqlDataSource>() is not {} source) return StatusCode(503);
        await using var db = await source.OpenConnectionAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<HistoryRow>(new CommandDefinition("""
            select m.id as MatchId,m.completed_at as CompletedAt,m.variant,m.rule_set as RuleSet,
            coalesce(p.finish_position=1,false) as Won,
            coalesce((select sum(x.amount) from uno.xp_transactions x where x.match_id=m.id and x.user_id=@Id),0) as Xp, m.match_mode as Mode, m.bot_difficulty as Difficulty, p.abandoned as Abandoned,
            case when m.match_mode='ranked' then coalesce((select sum(x.amount) from uno.xp_transactions x where x.match_id=m.id and x.user_id=@Id),0) else 0 end as SeasonalXp
            from uno.matches m join uno.match_players p on p.match_id=m.id
            where m.id=@MatchId and p.user_id=@Id and m.status='completed' and m.completed_at is not null
            """, new { MatchId=matchId, Id=id }, cancellationToken:ct));
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int offset = 0, [FromQuery] int limit = 20, [FromQuery] string mode = "all", CancellationToken ct = default)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var id)) return Unauthorized();
        if (mode is not ("all" or "bots" or "rooms" or "ranked") || offset < 0 || limit is < 1 or > 100) return BadRequest();
        if (services.GetService<NpgsqlDataSource>() is not {} source) return StatusCode(503);
        await using var db = await source.OpenConnectionAsync(ct);
        return Ok(await db.QueryAsync<HistoryRow>(new CommandDefinition("""
            select m.id as MatchId, m.completed_at as CompletedAt, m.variant, m.rule_set as RuleSet,
                coalesce(p.finish_position=1,false) as Won,
                coalesce((select sum(x.amount) from uno.xp_transactions x where x.match_id=m.id and x.user_id=@Id),0) as Xp, m.match_mode as Mode, m.bot_difficulty as Difficulty, p.abandoned as Abandoned,
            case when m.match_mode='ranked' then coalesce((select sum(x.amount) from uno.xp_transactions x where x.match_id=m.id and x.user_id=@Id),0) else 0 end as SeasonalXp
            from uno.matches m join uno.match_players p on p.match_id=m.id
            where p.user_id=@Id and m.status='completed' and m.completed_at is not null
            and (@Mode='all' or m.match_mode=@Mode)
            order by m.completed_at desc,m.id desc offset @Offset limit @Limit
            """, new { Id = id, Offset = offset, Limit = limit, Mode = mode }, cancellationToken: ct)));
    }
}
