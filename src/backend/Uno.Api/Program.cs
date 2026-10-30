using Uno.Api.Hubs;
using Uno.Api.Rooms;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
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

app.MapControllers();
app.MapHub<UnoRoomHub>("/hubs/uno-room");

app.Run();
