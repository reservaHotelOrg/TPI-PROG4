using System.Security.Claims;
using Application.Models;
using Domain;

namespace Web.Extensions;

public static class ClaimsPrincipalExtensions
{
    // Convierte el usuario del token (User dentro de un controller) en un CurrentUser
    // que entienden los servicios. Solo se usa en endpoints con [Authorize],
    // así que el token ya fue validado y trae estos datos.
    public static CurrentUser ToCurrentUser(this ClaimsPrincipal principal)
    {
        var idValue = principal.FindFirst("sub")?.Value;
        var roleValue = principal.FindFirst("role")?.Value;

        if (!int.TryParse(idValue, out int id) || !Enum.TryParse(roleValue, out Role role))
            throw new InvalidOperationException("El token no tiene un id o un rol válido.");

        return new CurrentUser(id, role);
    }
}
