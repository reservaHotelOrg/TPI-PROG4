using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Application.Interfaces;
using Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

// Arma el JWT: un texto firmado con la SecretKey que lleva adentro quién es el usuario
// y su rol. La API después verifica la firma en cada request (configurado en Program.cs).
public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Claims: datos clave -> valor que viajan dentro del token.
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("given_name", user.Name),
            new("family_name", user.LastName),
            new("email", user.Mail),
            new("role", user.Type.ToString())   // "SuperAdmin", "Administrator" o "Client"
        };

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes),
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
