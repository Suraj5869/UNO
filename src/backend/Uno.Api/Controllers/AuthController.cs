using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using Uno.Api.Authentication;

namespace Uno.Api.Controllers;

public sealed record RegisterRequest([Required, EmailAddress, MaxLength(254)] string Email,
    [Required, RegularExpression("^[A-Za-z0-9_]{3,24}$")] string Username,
    [Required, StringLength(24, MinimumLength = 1)] string DisplayName,
    [Required, StringLength(128, MinimumLength = 8)] string Password);
public sealed record LoginRequest([Required, MaxLength(254)] string Identifier, [Required, MaxLength(128)] string Password);
public sealed record RefreshRequest([Required, RegularExpression("^[A-Fa-f0-9]{64}$")] string RefreshToken);
public sealed record ProfileUpdateRequest([Required, StringLength(24, MinimumLength = 1)] string DisplayName);
public sealed record CosmeticSelection([Required, MaxLength(80)] string ItemKey);
public sealed record CosmeticView(string ItemKey, string Category, string DisplayName, bool Owned, bool Equipped, int RequiredLevel, int RequiredSeasonXp);
public sealed record AccountIdentity(Guid Id);
public sealed record SessionRow(Guid Id, Guid UserId);
public sealed record AccountRow(Guid Id, string PasswordHash);
public sealed record PlayerProfile(Guid UserId, Guid PublicPlayerId, string Email, string Username, string DisplayName,
    string AvatarKey, int Level, long TotalXp, int MatchesPlayed, int Wins, string CardBackKey, string TableThemeKey);
public sealed record AuthResponse(string AccessToken, string RefreshToken, int ExpiresIn, PlayerProfile Player);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(IServiceProvider services, PasswordHasher<AccountIdentity> hasher) : ControllerBase
{
    private NpgsqlDataSource? Source => services.GetService<NpgsqlDataSource>();
    private JwtSettings? Jwt => services.GetService<JwtSettings>();
    private const string ProfileSql = """
        select u.id as UserId, p.public_player_id as PublicPlayerId, u.email, p.username,
        p.display_name as DisplayName, p.avatar_key as AvatarKey, g.level, g.total_xp as TotalXp,
        s.matches_played as MatchesPlayed, s.wins,
        coalesce((select c.item_key from uno.player_loadouts l join uno.cosmetic_items c on c.id=l.item_id where l.user_id=u.id and l.category='card_back'),'card_back_classic') as CardBackKey,
        coalesce((select c.item_key from uno.player_loadouts l join uno.cosmetic_items c on c.id=l.item_id where l.user_id=u.id and l.category='table_theme'),'table_theme_default') as TableThemeKey
        from uno.users u join uno.player_profiles p on p.user_id=u.id
        join uno.player_progress g on g.user_id=u.id
        join uno.player_statistics s on s.user_id=u.id where u.id=@Id
        """;

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (Source is not {} source || Jwt is not {} jwt) return StatusCode(503);
        var display = request.DisplayName.Trim();
        if (display.Length == 0) return BadRequest(new { message = "Display name is required." });
        var id = Guid.NewGuid();
        var email = request.Email.Trim().ToLowerInvariant();
        var username = request.Username.ToLowerInvariant();
        var hash = hasher.HashPassword(new AccountIdentity(id), request.Password);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                insert into uno.users(id,email,password_hash) values (@Id,@Email,@Hash);
                insert into uno.player_profiles(user_id,username,display_name) values (@Id,@Username,@Display);
                insert into uno.player_progress(user_id) values (@Id);
                insert into uno.player_statistics(user_id) values (@Id);
                insert into uno.player_inventory(user_id,item_id)
                  select @Id,id from uno.cosmetic_items where item_key in ('avatar_default','card_back_classic','table_theme_default');
                insert into uno.player_loadouts(user_id,category,item_id)
                  select @Id,category,id from uno.cosmetic_items where item_key in ('avatar_default','card_back_classic','table_theme_default');
                """, new { Id=id, Email=email, Hash=hash, Username=username, Display=display }, tx, cancellationToken:ct));
            var response = await Issue(connection, tx, jwt, id, ct);
            await tx.CommitAsync(ct);
            return StatusCode(201, response);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { message = "Unable to create an account with these details." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (Source is not {} source || Jwt is not {} jwt) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        var identifier = request.Identifier.Trim().ToLowerInvariant();
        var attempts = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            insert into uno.login_attempts(identifier_hash,attempts,window_start) values(@Hash,1,now())
            on conflict(identifier_hash) do update set
            attempts=case when uno.login_attempts.window_start<now()-interval '15 minutes' then 1 else least(uno.login_attempts.attempts+1,1000) end,
            window_start=case when uno.login_attempts.window_start<now()-interval '15 minutes' then now() else uno.login_attempts.window_start end
            returning attempts
            """,new { Hash=JwtSettings.Hash(identifier) },cancellationToken:ct));
        if(attempts>5) return StatusCode(429,new { message="Too many login attempts. Try again later." });
        var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition("""
            select u.id, u.password_hash as PasswordHash from uno.users u
            join uno.player_profiles p on p.user_id=u.id
            where lower(trim(u.email))=@Identifier or lower(p.username)=@Identifier
            """, new { Identifier=identifier }, cancellationToken:ct));
        if (account is null)
        {
            // Perform comparable password hashing work for unknown accounts.
            hasher.HashPassword(new AccountIdentity(Guid.Empty), request.Password);
            return Unauthorized(new { message = "Invalid credentials." });
        }
        var verification = hasher.VerifyHashedPassword(new AccountIdentity(account.Id), account.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed) return Unauthorized(new { message = "Invalid credentials." });
        await connection.ExecuteAsync(new CommandDefinition("delete from uno.login_attempts where identifier_hash=@Hash",new { Hash=JwtSettings.Hash(identifier) },cancellationToken:ct));
        await using var tx = await connection.BeginTransactionAsync(ct);
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            await connection.ExecuteAsync(new CommandDefinition("update uno.users set password_hash=@Hash, updated_at=now() where id=@Id",
                new { account.Id, Hash=hasher.HashPassword(new AccountIdentity(account.Id),request.Password) }, tx,cancellationToken:ct));
        var response = await Issue(connection, tx, jwt, account.Id, ct);
        await tx.CommitAsync(ct);
        return Ok(response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        if (Source is not {} source || Jwt is not {} jwt) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var hash = JwtSettings.Hash(request.RefreshToken);
        var session = await connection.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition("""
            select s.id,s.user_id as UserId from uno.auth_sessions s
            join uno.refresh_tokens t on t.session_id=s.id
            where t.token_hash=@Hash and s.revoked_at is null and s.expires_at>now()
            for update of s
            """,new { Hash=hash },tx,cancellationToken:ct));
        if(session is null) return Unauthorized(new { message="Invalid or expired refresh token." });
        var used = await connection.ExecuteAsync(new CommandDefinition("""
            update uno.refresh_tokens set revoked_at=now()
            where token_hash=@Hash and revoked_at is null and expires_at>now()
            """,new { Hash=hash },tx,cancellationToken:ct));
        if(used!=1)
        {
            await connection.ExecuteAsync(new CommandDefinition("update uno.auth_sessions set revoked_at=now() where id=@Id",new { session.Id },tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            return Unauthorized(new { message="Session expired. Please sign in again." });
        }
        var response = await Issue(connection,tx,jwt,session.UserId,ct,session.Id);
        await tx.CommitAsync(ct);
        return Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        if (Source is not {} source) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("update uno.auth_sessions set revoked_at=now() where id=(select session_id from uno.refresh_tokens where token_hash=@Hash) and revoked_at is null",
            new { Hash=JwtSettings.Hash(request.RefreshToken) }, cancellationToken:ct));
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var id)) return Unauthorized();
        if (Source is not {} source) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        var profile = await connection.QuerySingleOrDefaultAsync<PlayerProfile>(new CommandDefinition(ProfileSql,new { Id=id }, cancellationToken:ct));
        return profile is null ? NotFound() : Ok(profile);
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(ProfileUpdateRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var id)) return Unauthorized();
        if (Source is not {} source) return StatusCode(503);
        var display = request.DisplayName.Trim();
        if (display.Length == 0) return BadRequest(new { message = "Display name is required." });
        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("update uno.player_profiles set display_name=@Display where user_id=@Id", new { Id=id, Display=display }, cancellationToken:ct));
        return await Me(ct);
    }

    [Authorize]
    [HttpGet("collection")]
    public async Task<IActionResult> Collection(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var id)) return Unauthorized();
        if (Source is not {} source) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        return Ok(await connection.QueryAsync<CosmeticView>(new CommandDefinition("""
            select c.item_key as ItemKey,c.category,c.display_name as DisplayName,
              (i.item_id is not null) as Owned,(l.item_id is not null) as Equipped,c.required_level as RequiredLevel,c.required_season_xp as RequiredSeasonXp
            from uno.cosmetic_items c
            left join uno.player_inventory i on i.item_id=c.id and i.user_id=@Id
            left join uno.player_loadouts l on l.item_id=c.id and l.user_id=@Id
            where c.is_active order by c.category,c.item_key
            """, new { Id=id }, cancellationToken:ct)));
    }

    [Authorize]
    [HttpPut("collection/equip")]
    public async Task<IActionResult> Equip(CosmeticSelection request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var id)) return Unauthorized();
        if (Source is not {} source) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var category = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            insert into uno.player_loadouts(user_id,category,item_id)
            select @Id,c.category,c.id from uno.cosmetic_items c
            join uno.player_inventory i on i.item_id=c.id and i.user_id=@Id
            where c.item_key=@Key and c.is_active
            on conflict(user_id,category) do update set item_id=excluded.item_id
            returning category
            """, new { Id=id, Key=request.ItemKey },tx,cancellationToken:ct));
        if (category is null) return BadRequest(new { message="This cosmetic is locked or unavailable." });
        if (category == "avatar")
            await connection.ExecuteAsync(new CommandDefinition("update uno.player_profiles set avatar_key=@Key where user_id=@Id",
                new { Id=id, Key=request.ItemKey },tx,cancellationToken:ct));
        var profile = await connection.QuerySingleAsync<PlayerProfile>(new CommandDefinition(ProfileSql,new { Id=id },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(profile);
    }

    private static async Task<AuthResponse> Issue(NpgsqlConnection connection, NpgsqlTransaction tx, JwtSettings jwt, Guid id, CancellationToken ct, Guid? existingSession = null)
    {
        var sessionId = existingSession ?? Guid.NewGuid();
        if(existingSession is null)
            await connection.ExecuteAsync(new CommandDefinition("insert into uno.auth_sessions(id,user_id,expires_at) values(@SessionId,@Id,now()+interval '30 days')",new { SessionId=sessionId,Id=id },tx,cancellationToken:ct));
        var refresh = JwtSettings.NewRefreshToken();
        await connection.ExecuteAsync(new CommandDefinition("""
            insert into uno.refresh_tokens(user_id,token_hash,expires_at,session_id) select @Id,@Hash,expires_at,id from uno.auth_sessions where id=@SessionId
            """, new { Id=id, Hash=JwtSettings.Hash(refresh), SessionId=sessionId },tx,cancellationToken:ct));
        var profile = await connection.QuerySingleAsync<PlayerProfile>(new CommandDefinition(ProfileSql,new { Id=id },tx,cancellationToken:ct));
        return new AuthResponse(jwt.Issue(id,sessionId),refresh,900,profile);
    }
}
