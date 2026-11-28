using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Uno.Api.Rooms;
using Uno.GameEngine;

namespace Uno.Api.Controllers;

[Authorize]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("social")]
[ApiController]
[Route("api/social")]
public sealed class SocialController(IServiceProvider services, RoomStore rooms) : ControllerBase
{
    private Guid Id => Guid.Parse(User.FindFirstValue("sub")!);
    private NpgsqlDataSource? Source => services.GetService<NpgsqlDataSource>();
    public sealed record Target(Guid PlayerId);
    public sealed record Invite(Guid PlayerId,string RoomCode,string AccessToken);
    public sealed record Decision(Guid InvitationId,bool Accept);
    private const string PublicFields = "p.public_player_id as \"playerId\",p.username,p.display_name as \"displayName\",p.avatar_key as \"avatarKey\",coalesce((select pr.level from uno.player_progress pr where pr.user_id=p.user_id),1) as level";
    [HttpGet("search")]
    public async Task<IActionResult> Search(string q, CancellationToken ct)
    {
        if(Source is not {} source)return StatusCode(503);
        q=q.Trim();
        if(q.Length<3 || q.Length>64)return BadRequest(new {message="Enter 3–64 characters."});
        await using var db=await source.OpenConnectionAsync(ct);
        return Ok(await db.QueryAsync(new CommandDefinition(
            "select "+PublicFields+" from uno.player_profiles p where p.user_id<>@Id and (strpos(lower(p.username),lower(@Q))>0 or p.public_player_id::text=@Q) order by p.username limit 20",
            new {Id,Q=q},cancellationToken:ct)));
    }
    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        if(Source is not {} source)return StatusCode(503);
        await using var db=await source.OpenConnectionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("insert into uno.social_presence(user_id) values(@Id) on conflict(user_id) do update set seen_at=now()",new {Id},cancellationToken:ct));
        var friends=await db.QueryAsync(new CommandDefinition("select "+PublicFields+"""
            ,case when f.status='accepted' then 'friend' when f.requester_id=@Id then 'outgoing' else 'incoming' end as "relationship",
            coalesce(f.status='accepted' and s.seen_at>now()-interval '45 seconds',false) as "online"
            from uno.friendships f join uno.player_profiles p on p.user_id=case when f.requester_id=@Id then f.recipient_id else f.requester_id end
            left join uno.social_presence s on s.user_id=p.user_id
            where @Id in(f.requester_id,f.recipient_id) and f.status in ('pending','accepted') order by p.username
            """,new {Id},cancellationToken:ct));
        var invitations=await db.QueryAsync(new CommandDefinition("select "+PublicFields+"""
            ,i.id as "invitationId",i.room_code as "roomCode" from uno.live_room_invitations i
            join uno.player_profiles p on p.user_id=i.sender_id
            where i.recipient_id=@Id and i.expires_at>now() order by i.created_at desc limit 30
            """,new {Id},cancellationToken:ct));
        return Ok(new {friends,invitations});
    }
    [HttpPost("request")]
    public async Task<IActionResult> RequestFriend(Target target,CancellationToken ct)
    {
        if(Source is not {} source)return StatusCode(503);
        await using var db=await source.OpenConnectionAsync(ct);
        var n=await db.ExecuteAsync(new CommandDefinition("""
            insert into uno.friendships(requester_id,recipient_id,status)
            select @Id,p.user_id,'pending' from uno.player_profiles p
            where p.public_player_id=@PlayerId and p.user_id<>@Id
            and not exists(select 1 from uno.friendships f where least(f.requester_id,f.recipient_id)=least(@Id,p.user_id) and greatest(f.requester_id,f.recipient_id)=greatest(@Id,p.user_id))
            on conflict do nothing
            """,new {Id,target.PlayerId},cancellationToken:ct));
        return n==1?NoContent():Conflict(new {message="Request already exists or player unavailable."});
    }
    [HttpPost("accept")]
    public async Task<IActionResult> Accept(Target target,CancellationToken ct)
    {
        if(Source is not {} source)return StatusCode(503);
        await using var db=await source.OpenConnectionAsync(ct);
        var n=await db.ExecuteAsync(new CommandDefinition("""
            update uno.friendships f set status='accepted',responded_at=now() from uno.player_profiles p
            where p.public_player_id=@PlayerId and least(f.requester_id,f.recipient_id)=least(@Id,p.user_id) and greatest(f.requester_id,f.recipient_id)=greatest(@Id,p.user_id)
            and f.requester_id=p.user_id and f.recipient_id=@Id and f.status='pending'
            """,new {Id,target.PlayerId},cancellationToken:ct));
        return n==1?NoContent():NotFound();
    }
    [HttpPost("remove")]
    public async Task<IActionResult> Remove(Target target,CancellationToken ct)
    {
        if(Source is not {} source)return StatusCode(503);
        await using var db=await source.OpenConnectionAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            delete from uno.friendships f using uno.player_profiles p
            where p.public_player_id=@PlayerId and least(f.requester_id,f.recipient_id)=least(@Id,p.user_id) and greatest(f.requester_id,f.recipient_id)=greatest(@Id,p.user_id);
            delete from uno.live_room_invitations i using uno.player_profiles p where p.public_player_id=@PlayerId
            and ((i.sender_id=@Id and i.recipient_id=p.user_id) or (i.recipient_id=@Id and i.sender_id=p.user_id));
            """,new {Id,target.PlayerId},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return NoContent();
    }
    [HttpPost("invite")]
    public async Task<IActionResult> InviteFriend(Invite request,CancellationToken ct)
    {
        if(Source is not {} source)return StatusCode(503);
        try {
            if(!rooms.CanInvite(request.RoomCode,request.AccessToken,Id,Guid.TryParse(User.FindFirstValue("sid"),out var sid) ? sid : null))return Forbid();
        } catch(KeyNotFoundException){return NotFound();} catch(UnauthorizedAccessException){return Forbid();}
        await using var db=await source.OpenConnectionAsync(ct);
        var n=await db.ExecuteAsync(new CommandDefinition("""
            insert into uno.live_room_invitations(sender_id,recipient_id,room_code)
            select @Id,p.user_id,@RoomCode from uno.player_profiles p join uno.friendships f
            on least(f.requester_id,f.recipient_id)=least(@Id,p.user_id) and greatest(f.requester_id,f.recipient_id)=greatest(@Id,p.user_id)
            where p.public_player_id=@PlayerId and f.status='accepted' and not exists(
              select 1 from uno.live_room_invitations i where i.sender_id=@Id and i.recipient_id=p.user_id and i.room_code=@RoomCode and i.expires_at>now())
            """,new {Id,request.PlayerId,request.RoomCode},cancellationToken:ct));
        return n==1?NoContent():Conflict(new {message="Friend unavailable or invitation already sent."});
    }
    [HttpPost("invitation")]
    public async Task<IActionResult> Respond(Decision request,CancellationToken ct)
    {
        if(Source is not {} source)return StatusCode(503);
        await using var db=await source.OpenConnectionAsync(ct);
        var code=await db.ExecuteScalarAsync<string>(new CommandDefinition("""
            delete from uno.live_room_invitations where id=@InvitationId and recipient_id=@Id and expires_at>now() returning room_code
            """,new {Id,request.InvitationId},cancellationToken:ct));
        if(code is null)return NotFound();
        return Ok(new {roomCode=request.Accept?code:null});
    }
}
