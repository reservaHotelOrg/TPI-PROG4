using System.Text;
using Application.Interfaces;
using Application.Services;
using Domain.Interfaces;
using Infrastructure.Data;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Web.Middlewares;
using Web.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    // Botón "Authorize" en Swagger para probar endpoints con token.
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

#region Database
// La cadena de conexión se lee de ConnectionStrings:DefaultConnection.
// En local viene de appsettings.json (archivo junto al proyecto) y en Azure de appsettings.Production.json
// (/home/data, fuera de la carpeta del sitio, para que un deploy no pise la base).
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
    throw new Exception("La cadena de conexión 'DefaultConnection' no está configurada en appsettings.json");

// SQLite crea el archivo .db pero no la carpeta: si no existe (por ejemplo /home/data en Azure), se crea.
var databaseFolder = Path.GetDirectoryName(Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource));
if (!string.IsNullOrEmpty(databaseFolder))
    Directory.CreateDirectory(databaseFolder);

var connection = new SqliteConnection(connectionString);
connection.Open();

// Set journal mode to DELETE using PRAGMA statement
using (var command = connection.CreateCommand())
{
    command.CommandText = "PRAGMA journal_mode = DELETE;";
    command.ExecuteNonQuery();
}

builder.Services.AddDbContext<ApplicationContext>(dbContextOptions => dbContextOptions.UseSqlite(connection));
#endregion

#region Authentication (JWT)
// Configuración del token, sección "Jwt" de appsettings.json.
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
if (jwtOptions is null || jwtOptions.SecretKey.Length < 32)
    throw new Exception("Falta configurar el JWT: revisá Jwt:SecretKey en appsettings.Development.json (local) o la variable de entorno Jwt__SecretKey (Azure), mínimo 32 caracteres");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// Cómo se valida el token que llega en el header Authorization: Bearer <token>.
// La fecha de vencimiento se valida por defecto.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Deja los claims con el nombre con que se escribieron ("sub", "role"...),
        // sin traducirlos a los nombres largos de Microsoft.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            // Así [Authorize(Roles = "...")] sabe en qué claim buscar el rol.
            RoleClaimType = "role",
            NameClaimType = "email"
        };
    });

builder.Services.AddAuthorization();
#endregion

#region HttpClient Factory (servicios externos)
// Cliente con nombre para DolarApi: la dirección base se configura una sola vez acá
// y DolarApiService lo pide con CreateClient("dolarApiHttpClient").
builder.Services.AddHttpClient(
    "dolarApiHttpClient",
    client =>
    {
        client.BaseAddress = new Uri("https://dolarapi.com/v1/");
    });
#endregion

#region Repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUserRepository, UserRepositoryEF>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IReservationRepository, ReservationRepository>();
builder.Services.AddScoped<IRoomRepository, RoomRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
#endregion

#region Services
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IDolarApiService, DolarApiService>();
#endregion

#region Seed
// Crea el primer SuperAdmin si no existe (sección "SuperAdminSeed": datos en appsettings.json,
// mail y contraseña en appsettings.Development.json en local (no se sube a git) y variables de entorno en Azure).
builder.Services.Configure<SuperAdminSeedOptions>(builder.Configuration.GetSection(SuperAdminSeedOptions.SectionName));
builder.Services.AddScoped<SuperAdminSeeder>();
#endregion

// Middleware global de manejo de excepciones (NotFound -> 404, Validation -> 400,
// credenciales inválidas -> 401, sin permiso -> 403, resto -> 500).
builder.Services.AddTransient<GlobalExceptionHandlingMiddleware>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // Crea la base y aplica las migraciones pendientes al arrancar.
    // En Azure nadie corre "dotnet ef database update", así que lo hace la API sola.
    scope.ServiceProvider.GetRequiredService<ApplicationContext>().Database.Migrate();

    scope.ServiceProvider.GetRequiredService<SuperAdminSeeder>().Seed();
}

// Configure the HTTP request pipeline.
// Swagger habilitado también fuera de Development, para poder probar la API publicada en Azure.
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "My API V1");
});

// Va primero para capturar las excepciones de todo lo que viene después.
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

// Primero se averigua QUIÉN es (lee y valida el token)...
app.UseAuthentication();
// ...y después si PUEDE entrar al endpoint ([Authorize]).
app.UseAuthorization();

app.MapControllers();

app.Run();
