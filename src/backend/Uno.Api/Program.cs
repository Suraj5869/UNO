using Uno.Api.Rooms;
using Uno.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// Room state is currently process-local. Use a persistent shared store before
// scaling the API to multiple instances.
builder.Services.AddSingleton<RoomStore>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<RoomRealtimePublisher>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHub<UnoRoomHub>("/hubs/uno-room");

app.Run();
