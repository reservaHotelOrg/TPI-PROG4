# Sistema de Gestión y Reservas de Hotel — API

API REST para gestionar habitaciones, reservas y pagos de un hotel.
Trabajo Práctico Integrador de **Programación IV** (TUP – UTN). Este backend es la base del TIF.

**Integrantes:** Lucía Romero · Leticia Soulos · Laura Suez

## Tecnologías
- .NET 10 · ASP.NET Core Web API
- Entity Framework Core (code first) · SQLite
- Autenticación JWT · contraseñas con BCrypt
- OpenAPI + Swagger UI
- GitHub Actions → Azure App Service
- Servicio externo: [DolarApi](https://dolarapi.com) consumido con HttpClient Factory

## Arquitectura
Clean Architecture en 4 proyectos dentro de `src/`:

| Capa | Contenido |
|---|---|
| **Domain** | Entidades, enums, excepciones e interfaces de repositorios (incluida `IRepository<T>` e `IUnitOfWork`) |
| **Application** | Servicios, DTOs, requests e interfaces de servicios |
| **Infrastructure** | `ApplicationContext`, repositorio genérico `Repository<T>`, repositorios específicos, Unit of Work, migraciones, JWT, BCrypt, DolarApi |
| **Web** | Controllers, middleware global de excepciones y configuración en `Program.cs` |

Las dependencias apuntan hacia adentro: Web depende de Infrastructure y Application, Infrastructure depende de Application, y Application depende de Domain.

### Repositorios y Unit of Work
- `Repository<T>` implementa el CRUD genérico (`GetById`, `List`, `Add`, `Update`, `Delete`).
- Cada repositorio específico hereda de él y agrega solo sus consultas propias (por ejemplo, `IsRoomAvailable` o `GetByMail`).
- Los repositorios **no guardan**: el servicio llama a `IUnitOfWork.SaveChanges()` al final de cada caso de uso, así una operación que toca varias entidades (acreditar un pago y confirmar la reserva) se guarda completa o no se guarda.

### Manejo de errores
Los servicios lanzan excepciones de dominio y `GlobalExceptionHandlingMiddleware` las traduce a respuestas `application/problem+json`:

| Excepción | Código HTTP |
|---|---|
| `AppValidationException` | 400 Bad Request |
| `InvalidCredentialsException` | 401 Unauthorized |
| `NotAllowedException` | 403 Forbidden |
| `NotFoundException` | 404 Not Found |
| Cualquier otra | 500 Internal Server Error |

## Modelo de dominio
![Modelo de dominio](docs/ModeloDeDominio.png)

Entidades: `User`, `Reservation`, `DetailReservation`, `Room` y `Payment`.
Enums: `Role` (SuperAdmin, Administrator, Client), `ReservationState` (Pending, Confirmed, Canceled) y `RoomType` (Single, Double, Suite, Deluxe).

## Reglas de negocio implementadas

**Usuarios**
- El mail es único (se guarda en minúsculas) y la contraseña se guarda hasheada con BCrypt.
- El registro público crea siempre un `Client`. Los usuarios `Administrator` o `SuperAdmin` solo los crea un SuperAdmin.
- Al iniciar la API, si no existe ningún SuperAdmin se crea uno con los datos de configuración.

**Habitaciones**
- Número único, precio por noche y capacidad mayores a cero.
- Una habitación fuera de servicio (`State = false`) no se puede reservar.
- Una habitación que figura en alguna reserva no se puede borrar (se pone fuera de servicio).

**Reservas**
- La reserva se crea junto con sus habitaciones (al menos una), a nombre del cliente logueado.
- Nace **pendiente** y con un código único generado por el sistema (`RSV-XXXXXX`).
- La fecha de inicio no puede ser pasada y la de fin tiene que ser posterior.
- Los huéspedes de cada habitación no pueden superar su capacidad.
- El total = noches × tarifa por noche de cada habitación (no depende de la cantidad de huéspedes) y lo calcula el sistema.
- No se puede reservar una habitación ocupada en esas fechas (control de overbooking). El día de check-out de una reserva puede ser el check-in de la siguiente.
- Un cliente solo ve y cancela sus propias reservas; el staff (Administrator y SuperAdmin) gestiona todas.
- Una reserva con pagos registrados no se borra: se cancela.

**Pagos**
- Todo pago pertenece a una reserva y nace **sin acreditar**. Solo el staff puede acreditarlo.
- Una reserva admite varios pagos parciales; la suma no puede superar el total.
- No se registran ni acreditan pagos de una reserva cancelada.
- Cuando los pagos acreditados llegan al **50 %** del total, la reserva pasa a **confirmada**.
- Un pago acreditado no se puede borrar.

## Endpoints
El detalle de cada endpoint (body y respuestas) está en Swagger: `/swagger`. Para probar los protegidos, iniciar sesión, copiar el token y pegarlo en el botón **Authorize**.

**Staff** = Administrator o SuperAdmin.

### Autenticación — `/api/authentication`
| Método | Ruta | Descripción | Acceso |
|---|---|---|---|
| POST | `/login` | Devuelve el token JWT y los datos del usuario | Público |

### Usuarios — `/api/users`
| Método | Ruta | Descripción | Acceso |
|---|---|---|---|
| POST | `/` | Registro (siempre crea un Client) | Público |
| GET | `/me` | Perfil del usuario logueado | Logueado |
| GET | `/{id}` | Usuario por id (un cliente solo puede ver el suyo) | Logueado |
| GET | `/` | Listado de usuarios | Staff |
| POST | `/staff` | Alta de usuario con rol elegido | SuperAdmin |

### Habitaciones — `/api/rooms`
| Método | Ruta | Descripción | Acceso |
|---|---|---|---|
| GET | `/` | Listado de habitaciones | Logueado |
| GET | `/available?start=AAAA-MM-DD&end=AAAA-MM-DD` | Habitaciones habilitadas y libres en esas fechas | Logueado |
| GET | `/{id}` | Habitación por id | Logueado |
| POST | `/` | Alta | Staff |
| PUT | `/{id}` | Modificación | Staff |
| DELETE | `/{id}` | Baja (solo si nunca fue reservada) | Staff |

### Reservas — `/api/reservations`
| Método | Ruta | Descripción | Acceso |
|---|---|---|---|
| POST | `/` | Crear reserva con sus habitaciones | Client |
| GET | `/me` | Reservas del usuario logueado | Logueado |
| GET | `/{id}` | Reserva por id (el cliente solo las suyas) | Logueado |
| GET | `/user/{userId}` | Reservas de un usuario (el cliente solo las suyas) | Logueado |
| PATCH | `/{id}/cancel` | Cancelar (el dueño o el staff) | Logueado |
| GET | `/` | Todas las reservas | Staff |
| PATCH | `/{id}/confirm` | Confirmar manualmente | Staff |
| DELETE | `/{id}` | Borrar (solo sin pagos) | Staff |

### Pagos — `/api/payments`
| Método | Ruta | Descripción | Acceso |
|---|---|---|---|
| POST | `/` | Registrar un pago de una reserva (el dueño o el staff) | Logueado |
| GET | `/{id}` | Pago por id (el cliente solo los de sus reservas) | Logueado |
| GET | `/reservation/{reservationId}` | Pagos de una reserva | Logueado |
| GET | `/` | Todos los pagos | Staff |
| PATCH | `/{id}/confirm` | Acreditar un pago | Staff |
| DELETE | `/{id}` | Borrar un pago sin acreditar | Staff |

### Dólar — `/api/dolar`
| Método | Ruta | Descripción | Acceso |
|---|---|---|---|
| GET | `/oficial` | Cotización del dólar oficial (servicio externo DolarApi) | Público |

## Cómo correrlo localmente
Requisitos: **.NET 10 SDK**. No hace falta instalar una base de datos.

1. Clonar el repo y pararse en `develop`.
2. Crear `src/Web/appsettings.Development.json` (no se sube a git):

   ```json
   {
     "Jwt": { "SecretKey": "<clave de 32 caracteres o más>" },
     "SuperAdminSeed": { "Mail": "<mail>", "Password": "<contraseña>" }
   }
   ```

3. Ejecutar `dotnet run --project src/Web` y abrir `http://localhost:5231/swagger`.

Al arrancar, la API aplica las migraciones, crea `src/Web/hotelDataBase.db` y el primer SuperAdmin.

## Migraciones
Desde la carpeta raíz del repo:

```bash
dotnet ef migrations add <Nombre> --project src/Infrastructure --startup-project src/Web
```

No hace falta correr `dotnet ef database update`: la API aplica las migraciones pendientes al iniciar.

## Despliegue
Cada push a `main` compila y despliega en Azure App Service con GitHub Actions (`.github/workflows/main_reservahotel.yml`).

En Azure la base se guarda fuera de la carpeta del sitio (`appsettings.Production.json`), para que un deploy no la pise.
Los secretos se configuran como variables de entorno del App Service: `Jwt__SecretKey`, `SuperAdminSeed__Mail` y `SuperAdminSeed__Password`.
