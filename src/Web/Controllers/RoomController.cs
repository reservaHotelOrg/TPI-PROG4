using Application.Interfaces;
using Application.Models;
using Application.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web.Controllers;

[Route("api/rooms")]
[ApiController]
[Authorize]
public class RoomController : ControllerBase
{
    private readonly IRoomService _roomService;

    public RoomController(IRoomService roomService)
    {
        _roomService = roomService;
    }

    [HttpGet]
    public ActionResult<List<RoomDto>> GetAll()
    {
        return Ok(_roomService.GetAllRooms());
    }

    [HttpGet("available")]
    public ActionResult<List<RoomDto>> GetAvailable(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end)
    {
        return Ok(_roomService.GetAvailableRooms(start, end));
    }

    [HttpGet("{id:int}")]
    public ActionResult<RoomDto> GetById([FromRoute] int id)
    {
        return Ok(_roomService.GetRoomById(id));
    }

    [HttpPost]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public ActionResult<RoomDto> Create([FromBody] RoomRequest request)
    {
        var created = _roomService.CreateRoom(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public ActionResult<RoomDto> Update(
        [FromRoute] int id,
        [FromBody] RoomRequest request)
    {
        return Ok(_roomService.UpdateRoom(id, request));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public IActionResult Delete([FromRoute] int id)
    {
        _roomService.DeleteRoom(id);
        return NoContent();
    }
}
