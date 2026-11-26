using Dapper;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Uno.Api.Rooms;

// Single API instance only. Checkpoints are private server data.
public sealed class RuntimeRecoveryService(RoomStore rooms,IServiceProvider services,ILogger<RuntimeRecoveryService> logger) : BackgroundService
{
    private NpgsqlDataSource? Source => services.GetService<NpgsqlDataSource>();
    private byte[] Key()
    {
        var value=services.GetRequiredService<IConfiguration>()["Security:CheckpointKey"];
        if(string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Security:CheckpointKey is required for database recovery.");
        var key=Convert.FromBase64String(value);
        if(key.Length!=32) throw new InvalidOperationException("Checkpoint key must be 32 bytes.");
        return key;
    }
    private sealed record Envelope(int Version,string Nonce,string Ciphertext,string Tag);
    private string Encrypt(string json)
    {
        var nonce=RandomNumberGenerator.GetBytes(12);
        var plain=Encoding.UTF8.GetBytes(json);
        var cipher=new byte[plain.Length];
        var tag=new byte[16];
        using var aes=new AesGcm(Key(),16);
        aes.Encrypt(nonce,plain,cipher,tag,Encoding.UTF8.GetBytes("uno-runtime-v1"));
        return JsonSerializer.Serialize(new Envelope(1,Convert.ToBase64String(nonce),Convert.ToBase64String(cipher),Convert.ToBase64String(tag)));
    }
    private string Decrypt(string json)
    {
        var envelope=JsonSerializer.Deserialize<Envelope>(json) ?? throw new InvalidOperationException("Invalid checkpoint envelope");
        if(envelope.Version!=1) throw new InvalidOperationException("Unsupported or unencrypted checkpoint; migrate offline before startup.");
        var cipher=Convert.FromBase64String(envelope.Ciphertext);
        var plain=new byte[cipher.Length];
        using var aes=new AesGcm(Key(),16);
        aes.Decrypt(Convert.FromBase64String(envelope.Nonce),cipher,Convert.FromBase64String(envelope.Tag),plain,Encoding.UTF8.GetBytes("uno-runtime-v1"));
        return Encoding.UTF8.GetString(plain);
    }
    public override async Task StartAsync(CancellationToken ct)
    {
        if(Source is {} source)
        {
            _ = Key();
            await using var db=await source.OpenConnectionAsync(ct);
            await db.ExecuteAsync(new CommandDefinition("""
                select match_mode,bot_difficulty from uno.matches limit 0;
                select abandoned from uno.match_players limit 0;
                select required_level,required_season_xp from uno.cosmetic_items limit 0;
                select user_id,amount,idempotency_key from uno.xp_transactions limit 0;
                select room_code from uno.live_room_invitations limit 0;
                """,cancellationToken:ct));
            var json=await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("select state::text from uno.runtime_checkpoint where id=1",cancellationToken:ct));
            if(json is not null) rooms.RestoreRuntime(Decrypt(json));
        }
        await base.StartAsync(ct);
    }
    private async Task Save(CancellationToken ct)
    {
        if(Source is not {} source) return;
        var json=Encrypt(rooms.ExportRuntime());
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
