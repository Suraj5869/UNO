using Uno.Api.Controllers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Uno.Api.Authentication;
using Npgsql;
using Dapper;
using Uno.Api.Hubs;
using Uno.Api.Rooms;

UnoApi.Build(args).Run();

public static class UnoApi
{
    public static WebApplication Build(string[] args, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllers().AddApplicationPart(typeof(Uno.Api.Controllers.RoomsController).Assembly);
        builder.Services.AddOpenApi();
        builder.Services.AddSwaggerGen();

        var configuredOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .GetChildren()
            .Select(section => section.Value)
            .OfType<string>()
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray();

        var allowedOrigins = configuredOrigins.Length > 0
            ? configuredOrigins
            : builder.Environment.IsDevelopment()
                ? new[] { "http://localhost:5173", "https://localhost:5173" }
                : Array.Empty<string>();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        // Room state is currently process-local. Use a persistent shared store before
        // scaling the API to multiple instances.
        builder.Services.AddSingleton<RoomStore>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RoomRealtimePublisher>();
        builder.Services.AddHostedService<UnoTurnTimeoutService>();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
            builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        builder.Services.AddSingleton<PasswordHasher<AccountIdentity>>();
        var jwtKey = builder.Configuration["Jwt:SigningKey"];
        var authentication = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
        if (!string.IsNullOrWhiteSpace(jwtKey))
        {
            if (System.Text.Encoding.UTF8.GetByteCount(jwtKey) < 32)
                throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
            var jwt = new JwtSettings(jwtKey);
            builder.Services.AddSingleton(jwt);
            authentication.AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = JwtSettings.Issuer,
                    ValidateAudience = true, ValidAudience = JwtSettings.Audience,
                    ValidateIssuerSigningKey = true, IssuerSigningKey = jwt.SigningKey,
                    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(15),
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
                };
            });
        }
        else
        {
            authentication.AddJwtBearer(options =>
            {
                options.Events = new JwtBearerEvents { OnMessageReceived = context =>
                {
                    context.NoResult();
                    return Task.CompletedTask;
                }};
            });
        }
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        configureServices?.Invoke(builder.Services);
        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseCors("Frontend");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapGet("/api/health/database", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var source = context.RequestServices.GetService<NpgsqlDataSource>();
            if (source is null)
                return Results.Json(new { status = "not_configured" }, statusCode: 503);
            try
            {
                await using var connection = await source.OpenConnectionAsync(cancellationToken);
                await connection.ExecuteScalarAsync<int>(new CommandDefinition("select 1", commandTimeout: 5, cancellationToken: cancellationToken));
                return Results.Json(new { status = "connected" });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(new { status = "unavailable" }, statusCode: 503);
            }
        });
        app.MapControllers();
        app.MapHub<UnoRoomHub>("/hubs/uno-room");

        return app;
    }
}
