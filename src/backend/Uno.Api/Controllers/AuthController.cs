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
public sealed record AccountRow(Guid Id, string PasswordHash);
public sealed record PlayerProfile(Guid UserId, Guid PublicPlayerId, string Email, string Username, string DisplayName,
    string AvatarKey, int Level, long TotalXp, int MatchesPlayed, int Wins);
public sealed record AuthResponse(string AccessToken, string RefreshToken, int ExpiresIn, PlayerProfile Player);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(IServiceProvider services, PasswordHasher<Guid> hasher) : ControllerBase
{
    private NpgsqlDataSource? Source => services.GetService<NpgsqlDataSource>();
    private JwtSettings? Jwt => services.GetService<JwtSettings>();
    private const string ProfileSql = """
        select u.id as UserId, p.public_player_id as PublicPlayerId, u.email, p.username,
        p.display_name as DisplayName, p.avatar_key as AvatarKey, g.level, g.total_xp as TotalXp,
        s.matches_played as MatchesPlayed, s.wins
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
        var hash = hasher.HashPassword(id, request.Password);
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
            return Conflict(new { message = "Email or username is already registered." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (Source is not {} source || Jwt is not {} jwt) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        var identifier = request.Identifier.Trim().ToLowerInvariant();
        var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition("""
            select u.id, u.password_hash as PasswordHash from uno.users u
            join uno.player_profiles p on p.user_id=u.id
            where lower(trim(u.email))=@Identifier or lower(p.username)=@Identifier
            """, new { Identifier=identifier }, cancellationToken:ct));
        if (account is null)
        {
            // Perform comparable password hashing work for unknown accounts.
            hasher.HashPassword(Guid.Empty, request.Password);
            return Unauthorized(new { message = "Invalid credentials." });
        }
        var verification = hasher.VerifyHashedPassword(account.Id, account.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed) return Unauthorized(new { message = "Invalid credentials." });
        await using var tx = await connection.BeginTransactionAsync(ct);
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            await connection.ExecuteAsync(new CommandDefinition("update uno.users set password_hash=@Hash, updated_at=now() where id=@Id",
                new { account.Id, Hash=hasher.HashPassword(account.Id,request.Password) }, tx,cancellationToken:ct));
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
        var id = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            update uno.refresh_tokens set revoked_at=now()
            where token_hash=@Hash and revoked_at is null and expires_at>now() returning user_id
            """, new { Hash=JwtSettings.Hash(request.RefreshToken) }, tx,cancellationToken:ct));
        if (id is null) return Unauthorized(new { message="Invalid or expired refresh token." });
        var response = await Issue(connection, tx, jwt, id.Value, ct);
        await tx.CommitAsync(ct);
        return Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        if (Source is not {} source) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("update uno.refresh_tokens set revoked_at=now() where token_hash=@Hash and revoked_at is null",
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

    private static async Task<AuthResponse> Issue(NpgsqlConnection connection, NpgsqlTransaction tx, JwtSettings jwt, Guid id, CancellationToken ct)
    {
        var refresh = JwtSettings.NewRefreshToken();
        await connection.ExecuteAsync(new CommandDefinition("""
            insert into uno.refresh_tokens(user_id,token_hash,expires_at) values (@Id,@Hash,now()+interval '30 days')
            """, new { Id=id, Hash=JwtSettings.Hash(refresh) },tx,cancellationToken:ct));
        var profile = await connection.QuerySingleAsync<PlayerProfile>(new CommandDefinition(ProfileSql,new { Id=id },tx,cancellationToken:ct));
        return new AuthResponse(jwt.Issue(id),refresh,900,profile);
    }
}
