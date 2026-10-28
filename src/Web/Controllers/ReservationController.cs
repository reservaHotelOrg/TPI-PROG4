using Application.Interfaces;
using Application.Models;
using Application.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Extensions;

namespace Web.Controllers;

// Solo traduce HTTP <-> servicio, acá no hay lógica.
// Los errores los resuelve el middleware (no encontrado -> 404, validación -> 400, sin permiso -> 403).
// Permisos:
// - [Authorize(Roles = ...)] corta por rol antes de entrar al método.
// - "¿Es su reserva?" lo decide el servicio con el CurrentUser (un cliente solo accede a lo suyo).
[Route("api/reservations")]
[ApiController]
[Authorize]
public class ReservationController : ControllerBase
{
    private readonly IReservationService _reservationService;

    public ReservationController(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    // Monitoreo de todas las reservas: solo staff.
    [HttpGet]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public ActionResult<List<ReservationDto>> GetAll()
    {
        return Ok(_reservationService.GetAllReservations());
    }

    // Las reservas del usuario logueado.
    [HttpGet("me")]
    public ActionResult<List<ReservationDto>> GetMine()
    {
        var currentUser = User.ToCurrentUser();
        return Ok(_reservationService.GetReservationsByUser(currentUser.Id, currentUser));
    }

    [HttpGet("{id:int}")]
    public ActionResult<ReservationDto> GetById([FromRoute] int id)
    {
        return Ok(_reservationService.GetReservationById(id, User.ToCurrentUser()));
    }

    [HttpGet("user/{userId:int}")]
    public ActionResult<List<ReservationDto>> GetByUser([FromRoute] int userId)
    {
        return Ok(_reservationService.GetReservationsByUser(userId, User.ToCurrentUser()));
    }

    // Realizar reserva (CU-01): la hace el cliente logueado, a su nombre.
    [HttpPost]
    [Authorize(Roles = "Client")]
    public ActionResult<ReservationDto> Create([FromBody] CreateReservationRequest request)
    {
        var created = _reservationService.CreateReservation(request, User.ToCurrentUser());
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPatch("{id:int}/confirm")]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public ActionResult<ReservationDto> Confirm([FromRoute] int id)
    {
        return Ok(_reservationService.ConfirmReservation(id));
    }

    // Cancelar: el cliente dueño de la reserva o el staff.
    [HttpPatch("{id:int}/cancel")]
    public ActionResult<ReservationDto> Cancel([FromRoute] int id)
    {
        return Ok(_reservationService.CancelReservation(id, User.ToCurrentUser()));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public IActionResult Delete([FromRoute] int id)
    {
        _reservationService.DeleteReservation(id);
        return NoContent();
    }
}
