using Application.Interfaces;
using Application.Models;
using Application.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Extensions;

namespace Web.Controllers;

// Solo traduce HTTP <-> servicio, acá no hay lógica.
// [Authorize] a nivel clase: todo endpoint pide token, salvo los marcados con [AllowAnonymous].
[Route("api/users")]
[ApiController]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    // Listado completo: solo staff.
    [HttpGet]
    [Authorize(Roles = "Administrator,SuperAdmin")]
    public ActionResult<List<UserDto>> GetAll()
    {
        return Ok(_userService.GetAllUsers());
    }

    // Perfil del usuario logueado.
    [HttpGet("me")]
    public ActionResult<UserDto> GetMe()
    {
        var currentUser = User.ToCurrentUser();
        return Ok(_userService.GetUserById(currentUser.Id, currentUser));
    }

    // Un cliente solo puede pedir su propio id (si no, 403); el staff, cualquiera.
    [HttpGet("{id:int}")]
    public ActionResult<UserDto> GetById([FromRoute] int id)
    {
        return Ok(_userService.GetUserById(id, User.ToCurrentUser()));
    }

    // Registro público (SignUp): no pide token y siempre crea un Client.
    [HttpPost]
    [AllowAnonymous]
    public ActionResult<UserDto> Create([FromBody] CreateUserRequest request)
    {
        var created = _userService.CreateUser(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    // Alta con rol elegido (por ejemplo, un Administrator): solo SuperAdmin.
    [HttpPost("staff")]
    [Authorize(Roles = "SuperAdmin")]
    public ActionResult<UserDto> CreateStaff([FromBody] CreateStaffUserRequest request)
    {
        var created = _userService.CreateStaffUser(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
