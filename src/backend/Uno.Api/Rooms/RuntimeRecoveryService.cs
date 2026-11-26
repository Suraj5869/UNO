using Dapper;
using Npgsql;

namespace Uno.Api.Rooms;

// Single API instance only. Checkpoints are private server data.
public sealed class RuntimeRecoveryService(RoomStore rooms,IServiceProvider services,ILogger<RuntimeRecoveryService> logger) : BackgroundService
{
    private NpgsqlDataSource? Source => services.GetService<NpgsqlDataSource>();
    public override async Task StartAsync(CancellationToken ct)
    {
        if(Source is {} source)
        {
            await using var db=await source.OpenConnectionAsync(ct);
            await db.ExecuteAsync(new CommandDefinition("""
                select match_mode,bot_difficulty from uno.matches limit 0;
                select abandoned from uno.match_players limit 0;
                select required_level,required_season_xp from uno.cosmetic_items limit 0;
                select user_id,amount,idempotency_key from uno.xp_transactions limit 0;
                select room_code from uno.live_room_invitations limit 0;
                """,cancellationToken:ct));
            var json=await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("select state::text from uno.runtime_checkpoint where id=1",cancellationToken:ct));
            if(json is not null) rooms.RestoreRuntime(json);
        }
        await base.StartAsync(ct);
    }
    private async Task Save(CancellationToken ct)
    {
        if(Source is not {} source) return;
        var json=rooms.ExportRuntime();
        await using var db=await source.OpenConnectionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            insert into uno.runtime_checkpoint(id,state) values(1,cast(@Json as jsonb))
            on conflict(id) do update set state=excluded.state,updated_at=now()
            """,new { Json=json },cancellationToken:ct));
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        while(await timer.WaitForNextTickAsync(ct))
        {
            try { rooms.CleanupEmptyRooms(); await Save(ct); }
            catch(Exception ex) when(ex is not OperationCanceledException) { logger.LogError(ex,"Runtime checkpoint failed"); }
        }
    }
    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        await Save(ct);
    }
}
