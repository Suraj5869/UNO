using Microsoft.AspNetCore.Mvc;
using Uno.Api.Rooms;

namespace Uno.Api.Controllers;

[ApiController]
[Route("api/rooms")]
public sealed class RoomsController(RoomStore roomStore, RoomRealtimePublisher publisher) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RoomAccessResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<RoomAccessResponse> CreateRoom(CreateRoomRequest request)
    {
        try
        {
            var access = roomStore.Create(request);
            var response = new RoomAccessResponse(
                RoomResponse.From(access.Room),
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
            var access = roomStore.Join(code, request.DisplayName);
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
                RoomResponse.From(access.Room),
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
