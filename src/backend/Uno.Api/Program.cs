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
        app.UseAuthorization();

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
