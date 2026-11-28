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
public sealed record ResendVerificationRequest([Required, EmailAddress, MaxLength(254)] string Email);
public sealed record VerifyEmailRequest([Required, RegularExpression("^[A-Fa-f0-9]{64}$")] string Token);
public sealed record LoginRequest([Required, MaxLength(254)] string Identifier, [Required, MaxLength(128)] string Password);
public sealed record RefreshRequest([RegularExpression("^[A-Fa-f0-9]{64}$")] string? RefreshToken);
public sealed record ProfileUpdateRequest([Required, StringLength(24, MinimumLength = 1)] string DisplayName);
public sealed record CosmeticSelection([Required, MaxLength(80)] string ItemKey);
public sealed record CosmeticView(string ItemKey, string Category, string DisplayName, bool Owned, bool Equipped, int RequiredLevel, int RequiredSeasonXp);
public sealed record AccountIdentity(Guid Id);
public sealed record SessionRow(Guid Id, Guid UserId);
public sealed record AccountRow(Guid Id, string PasswordHash, bool EmailVerified, string Email, bool VerificationRequested);
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
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await connection.ExecuteAsync(new CommandDefinition("insert into uno.email_verification_tokens(user_id,token_hash,expires_at) values (@Id,@Hash,now()+interval '30 minutes')",new { Id=id, Hash=JwtSettings.Hash(token) },tx,cancellationToken:ct));
            await tx.CommitAsync(ct);
            try { await SendVerification(email, token, ct); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            { services.GetRequiredService<ILogger<AuthController>>().LogWarning("Verification email delivery failed. Type: {Type}, Status: {Status}", ex.GetType().Name, (ex as HttpRequestException)?.StatusCode); }
            return StatusCode(202, new { message = "Check your email to verify your account." });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { message = "Unable to create an account with these details." });
        }
    }

    private async Task SendVerification(string email, string token, CancellationToken ct)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var key = config["Email:BrevoApiKey"];
        var sender = config["Email:Sender"];
        var frontend = config["Email:FrontendUrl"];
        var environment = services.GetRequiredService<IWebHostEnvironment>();

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Brevo API key is missing.");

        if (string.IsNullOrWhiteSpace(sender))
            throw new InvalidOperationException("Email sender is missing.");

        if (!Uri.TryCreate(frontend, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Invalid frontend URL.");

        var isAllowedLocalUrl =
            environment.IsDevelopment() &&
            uri.IsLoopback &&
            uri.Scheme == Uri.UriSchemeHttp;

        if (uri.Scheme != Uri.UriSchemeHttps && !isAllowedLocalUrl)
            throw new InvalidOperationException(
                "Frontend URL must use HTTPS.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        message.Headers.Add("api-key", key);
        message.Content = System.Net.Http.Json.JsonContent.Create(new { sender = new { email = sender }, to = new[] { new { email } }, subject = "Verify your UNO account", textContent = "Verify your email within 30 minutes: " + frontend!.TrimEnd('/') + "/#verify=" + token });
        using var result = await client.SendAsync(message, ct);
        result.EnsureSuccessStatusCode();
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken ct)
    {
        if (Source is not {} source) return StatusCode(503);
        var email = request.Email.Trim().ToLowerInvariant();
        await RequestVerification(source, email, ct);
        return Accepted(new { message = "If an eligible account exists, a verification email will be sent." });
    }

    private async Task<string> RequestVerification(NpgsqlDataSource source, string email, CancellationToken ct)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using var db = await source.OpenConnectionAsync(ct);
        var changed = await db.ExecuteAsync(new CommandDefinition("""
            insert into uno.email_verification_tokens(user_id,token_hash,expires_at)
            select id,@Hash,now()+interval '30 minutes' from uno.users
            where lower(email)=@Email and email_verified_at is null
            on conflict(user_id) do update set token_hash=excluded.token_hash,
            expires_at=excluded.expires_at,created_at=now()
            where uno.email_verification_tokens.created_at < now()-interval '2 minutes'
            """,new { Email=email, Hash=JwtSettings.Hash(token) },cancellationToken:ct));
        if (changed != 1) { services.GetRequiredService<ILogger<AuthController>>().LogInformation("Verification request skipped: cooldown or account already verified."); return "cooldown"; }
        try
        {
            await SendVerification(email, token, ct);
            services.GetRequiredService<ILogger<AuthController>>().LogInformation("Verification email accepted by provider.");
            return "sent";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            services.GetRequiredService<ILogger<AuthController>>().LogWarning("Verification email delivery failed. Type: {Type}, Status: {Status}", ex.GetType().Name, (ex as HttpRequestException)?.StatusCode);
            await db.ExecuteAsync(new CommandDefinition("delete from uno.email_verification_tokens where token_hash=@Hash", new { Hash=JwtSettings.Hash(token) }, cancellationToken:CancellationToken.None));
            return "failed";
        }
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        if (Source is not {} source) return StatusCode(503);
        await using var db = await source.OpenConnectionAsync(ct);
        var changed = await db.ExecuteAsync(new CommandDefinition("""
            with consumed as (
                delete from uno.email_verification_tokens where token_hash=@Hash and expires_at>now() returning user_id
            )
            update uno.users set email_verified_at=now() where id in (select user_id from consumed)
            """,new { Hash=JwtSettings.Hash(request.Token) },cancellationToken:ct));
        return changed == 1 ? Ok(new { message = "Email verified. Sign in to continue." }) : BadRequest(new { message = "Invalid or expired verification link." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (Source is not {} source || Jwt is not {} jwt) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        var identifier = request.Identifier.Trim().ToLowerInvariant();
        // Partition identifier throttling by source to avoid global victim lockout.
        var attemptKey = JwtSettings.Hash(identifier + "\n" + (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"));
        // Bound storage for expired login-attempt counters.
        await connection.ExecuteAsync(new CommandDefinition("""
            delete from uno.login_attempts where identifier_hash in
            (select identifier_hash from uno.login_attempts where window_start<now()-interval '1 day' limit 100)
            """,cancellationToken:ct));
        var attempts = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            insert into uno.login_attempts(identifier_hash,attempts,window_start) values(@Hash,1,now())
            on conflict(identifier_hash) do update set
            attempts=case when uno.login_attempts.window_start<now()-interval '15 minutes' then 1 else least(uno.login_attempts.attempts+1,1000) end,
            window_start=case when uno.login_attempts.window_start<now()-interval '15 minutes' then now() else uno.login_attempts.window_start end
            returning attempts
            """,new { Hash=attemptKey },cancellationToken:ct));
        if(attempts>5) return StatusCode(429,new { message="Too many login attempts. Try again later." });
        var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition("""
            select u.id, u.password_hash as PasswordHash, u.email_verified_at is not null as EmailVerified, u.email, exists(select 1 from uno.email_verification_tokens v where v.user_id=u.id) as VerificationRequested from uno.users u
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
        await connection.ExecuteAsync(new CommandDefinition("delete from uno.login_attempts where identifier_hash=@Hash",new { Hash=attemptKey },cancellationToken:ct));
        if (!account.EmailVerified)
        {
            services.GetRequiredService<ILogger<AuthController>>().LogInformation("Unverified login: requesting verification email.");
            var delivery = await RequestVerification(source, account.Email.Trim().ToLowerInvariant(), ct);
            return StatusCode(403, new { code = "EMAIL_NOT_VERIFIED", message = "Verify your email before signing in.", email = account.Email, verificationRequested = delivery == "sent", delivery });
        }
        await using var tx = await connection.BeginTransactionAsync(ct);
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            await connection.ExecuteAsync(new CommandDefinition("update uno.users set password_hash=@Hash, updated_at=now() where id=@Id",
                new { account.Id, Hash=hasher.HashPassword(new AccountIdentity(account.Id),request.Password) }, tx,cancellationToken:ct));

        var response = await Issue(connection, tx, jwt, account.Id, ct);
        await tx.CommitAsync(ct);
        return Ok(BrowserResponse(response));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        if (Source is not {} source || Jwt is not {} jwt) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var token = Request.Headers["X-Uno-Browser"] == "1" ? Request.Cookies["__Host-uno-refresh"] : request.RefreshToken;
        if (token is null || token.Length != 64) return Unauthorized();
        var hash = JwtSettings.Hash(token);
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
            services.GetService<Uno.Api.Rooms.RoomStore>()?.RevokeSessionRooms(session.UserId, session.Id);
            return Unauthorized(new { message="Session expired. Please sign in again." });
        }
        var response = await Issue(connection,tx,jwt,session.UserId,ct,session.Id);
        await tx.CommitAsync(ct);
        return Ok(BrowserResponse(response));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        if (Source is not {} source) return StatusCode(503);
        await using var connection = await source.OpenConnectionAsync(ct);
        var revokedSession = await connection.QuerySingleOrDefaultAsync<SessionRow>(new CommandDefinition("update uno.auth_sessions set revoked_at=now() where id=(select session_id from uno.refresh_tokens where token_hash=@Hash) and revoked_at is null returning id,user_id as UserId",
            new { Hash=JwtSettings.Hash((Request.Headers["X-Uno-Browser"] == "1" ? Request.Cookies["__Host-uno-refresh"] : request.RefreshToken) ?? "") }, cancellationToken:ct));
        if (revokedSession is not null) services.GetService<Uno.Api.Rooms.RoomStore>()?.RevokeSessionRooms(revokedSession.UserId, revokedSession.Id);
        Response.Cookies.Delete("__Host-uno-refresh", CookieSettings());
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

    private static CookieOptions CookieSettings() => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/"
    };

    private AuthResponse BrowserResponse(AuthResponse response)
    {
        if (Request.Headers["X-Uno-Browser"] != "1") return response;
        var options = CookieSettings();
        options.MaxAge = TimeSpan.FromDays(30);
        Response.Cookies.Append("__Host-uno-refresh", response.RefreshToken, options);
        return response with { RefreshToken = "" };
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
