using Application.Interfaces;
using Application.Models;
using Application.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Extensions;

namespace Web.Controllers;

// Controller delgado: solo traduce HTTP <-> servicio. Los errores los resuelve
// GlobalExceptionHandlingMiddleware (404, 400, 403).
// Permisos: el cliente ve y registra pagos de sus reservas; acreditar y borrar es del staff.
[Route("api/payments")]
[ApiController]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public ActionResult<List<PaymentDto>> GetAll()
    {
        return Ok(_paymentService.GetAllPayments());
    }

    [HttpGet("{id:int}")]
    public ActionResult<PaymentDto> GetById([FromRoute] int id)
    {
        return Ok(_paymentService.GetPaymentById(id, User.ToCurrentUser()));
    }

    [HttpGet("reservation/{reservationId:int}")]
    public ActionResult<List<PaymentDto>> GetByReservation([FromRoute] int reservationId)
    {
        return Ok(_paymentService.GetPaymentsByReservation(reservationId, User.ToCurrentUser()));
    }

    // Registrar un pago: el dueño de la reserva o el staff (por ejemplo, pago en destino).
    [HttpPost]
    public ActionResult<PaymentDto> Register([FromBody] CreatePaymentRequest request)
    {
        var created = _paymentService.RegisterPayment(request, User.ToCurrentUser());
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    // Acreditar un pago: solo staff (un cliente no puede acreditarse su propia seña).
    [HttpPatch("{id:int}/confirm")]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public ActionResult<PaymentDto> Confirm([FromRoute] int id)
    {
        return Ok(_paymentService.ConfirmPayment(id));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public IActionResult Delete([FromRoute] int id)
    {
        _paymentService.DeletePayment(id);
        return NoContent();
    }
}
