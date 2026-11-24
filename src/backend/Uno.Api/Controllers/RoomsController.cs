using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Uno.Api.Rooms;
using Uno.GameEngine;

namespace Uno.Api.Controllers;

[ApiController]
[Route("api/rooms")]
public sealed class RoomsController(RoomStore roomStore, RoomRealtimePublisher publisher) : ControllerBase
{
    private Guid? AccountId => Guid.TryParse(User.FindFirstValue("sub"), out var id) && User.Identity?.IsAuthenticated == true ? id : null;

    [HttpPost]
    [ProducesResponseType<RoomAccessResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<RoomAccessResponse> CreateRoom(CreateRoomRequest request)
    {
        try
        {
            var access = roomStore.Create(request, AccountId);
            var response = new RoomAccessResponse(
                roomStore.ExecuteLocked(access.Room.Code, currentRoom => RoomResponse.From(currentRoom)),
                access.PlayerId,
                access.AccessToken);
            return CreatedAtAction(
                nameof(GetRoom),
                new { code = access.Room.Code },
                response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(Problem("Invalid room request", exception.Message, 400));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem("Could not create room", exception.Message, 409));
        }
    }

    public sealed record PracticeRequest(string DisplayName,int BotCount,BotDifficulty Difficulty,UnoRuleSet RuleSet);
    public sealed record AddBotsRequest(string AccessToken,int BotCount,BotDifficulty Difficulty);

    [HttpPost("practice")]
    public ActionResult<RoomAccessResponse> Practice(PracticeRequest request)
    {
        try {
            var access=roomStore.CreatePractice(request.DisplayName,request.BotCount,request.Difficulty,request.RuleSet,AccountId);
            return Ok(new RoomAccessResponse(roomStore.ExecuteLocked(access.Room.Code,RoomResponse.From),access.PlayerId,access.AccessToken));
        } catch(ArgumentException ex) { return BadRequest(Problem("Invalid practice settings",ex.Message,400)); }
        catch(InvalidOperationException ex) { return Conflict(Problem("Cannot create practice",ex.Message,409)); }
    }

    [HttpPost("{code}/bots")]
    public async Task<ActionResult<RoomResponse>> AddBots(string code,AddBotsRequest request)
    {
        try {
            roomStore.AddBots(code,request.AccessToken,request.BotCount,request.Difficulty);
            await publisher.PublishRoomAsync(code);
            return Ok(roomStore.ExecuteLocked(code,RoomResponse.From));
        } catch(UnauthorizedAccessException ex) { return StatusCode(403,Problem("Host required",ex.Message,403)); }
        catch(KeyNotFoundException ex) { return NotFound(Problem("Room not found",ex.Message,404)); }
        catch(ArgumentException ex) { return BadRequest(Problem("Invalid bot settings",ex.Message,400)); }
        catch(InvalidOperationException ex) { return Conflict(Problem("Cannot add bots",ex.Message,409)); }
    }

    public sealed record MatchmakingRequest(string DisplayName, int MaximumPlayers = 2, UnoRuleSet RuleSet = UnoRuleSet.Official);

    [HttpPost("matchmaking")]
    public async Task<ActionResult<RoomAccessResponse>> FindMatch(MatchmakingRequest request)
    {
        try
        {
            var access = roomStore.FindMatch(request.DisplayName, AccountId, request.MaximumPlayers, request.RuleSet);
            var response = roomStore.ExecuteLocked(access.Room.Code, RoomResponse.From);
            await publisher.PublishRoomAsync(access.Room.Code);
            if (response.GameStatus is not null)
                await publisher.PublishGameStateAsync(access.Room.Code);
            return Ok(new RoomAccessResponse(response, access.PlayerId, access.AccessToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(Problem("Invalid match request", exception.Message, 400));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem("Could not find match", exception.Message, 409));
        }
    }

    [HttpGet("{code}")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<RoomResponse> GetRoom(string code)
    {
        try
        {
            return Ok(roomStore.ExecuteLocked(code, room => RoomResponse.From(room)));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(Problem("Room not found", exception.Message, 404));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(Problem("Invalid room code", exception.Message, 400));
        }
    }

    [HttpPost("{code}/join")]
    [ProducesResponseType<RoomAccessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomAccessResponse>> JoinRoom(string code, JoinRoomRequest request)
    {
        try
        {
            var access = roomStore.Join(code, request.DisplayName, AccountId);
            var roomResponse = roomStore.ExecuteLocked(code, room => RoomResponse.From(room));
            await publisher.PublishRoomAsync(code);
            return Ok(new RoomAccessResponse(
                roomResponse,
                access.PlayerId,
                access.AccessToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(Problem("Room not found", exception.Message, 404));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(Problem("Invalid join request", exception.Message, 400));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem("Cannot join room", exception.Message, 409));
        }
    }

    [HttpPut("{code}/rules")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomResponse>> ChangeRules(
        string code,
        ChangeRoomRulesRequest request)
    {
        try
        {
            var room = roomStore.ExecuteLocked(code, currentRoom =>
            {
                var playerId = roomStore.AuthorizeParticipant(code, request.AccessToken);
                if (roomStore.IsSearching(code)) throw new InvalidOperationException("Public queue settings cannot change.");
                currentRoom.SetRuleSet(playerId, request.RuleSet);
                return RoomResponse.From(currentRoom);
            });
            await publisher.PublishRoomAsync(code);
            return Ok(room);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(Problem("Room not found", exception.Message, 404));
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Problem("Room permission required", exception.Message, 403));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(Problem("Invalid rules selection", exception.Message, 400));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem("Cannot change room rules", exception.Message, 409));
        }
    }

    [HttpPost("{code}/start")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomResponse>> StartGame(string code, StartRoomRequest request)
    {
        try
        {
            roomStore.StartGame(code, request.AccessToken);
            var room = roomStore.ExecuteLocked(code, currentRoom => RoomResponse.From(currentRoom));
            await publisher.PublishRoomAsync(code);
            await publisher.PublishGameStateAsync(code);
            return Ok(room);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(Problem("Room not found", exception.Message, 404));
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Problem("Room permission required", exception.Message, 403));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem("Cannot start game", exception.Message, 409));
        }
    }

    [HttpPost("{code}/play-again")]
    [ProducesResponseType<RoomAccessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoomAccessResponse>> PlayAgain(string code, StartRoomRequest request)
    {
        try
        {
            var access = roomStore.PlayAgain(code, request.AccessToken);
            var response = new RoomAccessResponse(
                roomStore.ExecuteLocked(access.Room.Code, currentRoom => RoomResponse.From(currentRoom)),
                access.PlayerId,
                access.AccessToken);
            await publisher.PublishRoomAsync(code);
            return Ok(response);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(Problem("Room not found", exception.Message, 404));
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Problem("Room permission required", exception.Message, 403));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Problem("Cannot start rematch", exception.Message, 409));
        }
    }

    private static ProblemDetails Problem(string title, string detail, int status) =>
        new()
        {
            Title = title,
            Detail = detail,
            Status = status
        };
}
