using Application.Interfaces;
using Domain;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Data;

// Datos de demo, sección "DemoSeed" de appsettings.json. Son usuarios de prueba, no secretos.
public class DemoSeedOptions
{
    public const string SectionName = "DemoSeed";

    public bool Enabled { get; set; }
    public string ClientPassword { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
}

// Carga datos de demo al arrancar la API si la base no tiene habitaciones,
// para que la base de Azure y las bases locales tengan los mismos datos en la defensa.
// Todo se crea con los constructores y métodos del dominio, así se cumplen las mismas
// reglas de negocio que cuando se carga por la API.
public class DemoDataSeeder
{
    private static readonly string[] DemoMails =
        { "recepcion@hotel.com", "ana@mail.com", "juan@mail.com", "maria@mail.com" };

    private readonly ApplicationContext _context;
    private readonly IUserRepository _userRepository;
    private readonly IRoomRepository _roomRepository;
    private readonly IReservationRepository _reservationRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly DemoSeedOptions _options;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        ApplicationContext context,
        IUserRepository userRepository,
        IRoomRepository roomRepository,
        IReservationRepository reservationRepository,
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IOptions<DemoSeedOptions> options,
        ILogger<DemoDataSeeder> logger)
    {
        _context = context;
        _userRepository = userRepository;
        _roomRepository = roomRepository;
        _reservationRepository = reservationRepository;
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _logger = logger;
    }

    public void Seed()
    {
        if (!_options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(_options.ClientPassword) || string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            _logger.LogWarning("DemoSeed habilitado pero sin contraseñas configuradas: no se cargan datos de demo");
            return;
        }

        // Idempotente: si ya hay habitaciones, la base ya tiene datos y no se toca.
        if (_roomRepository.List().Any())
            return;

        if (DemoMails.Any(mail => _userRepository.GetByMail(mail) is not null))
        {
            _logger.LogWarning("Ya existen usuarios con los mails de demo: no se cargan datos de demo");
            return;
        }

        // Hacen falta varios SaveChanges (los ids se generan al guardar), así que todo va
        // dentro de una transacción: si algo falla, no queda la base a medio cargar.
        using var transaction = _context.Database.BeginTransaction();
        try
        {
            // 1. Usuarios y habitaciones
            string clientHash = _passwordHasher.Hash(_options.ClientPassword);

            _userRepository.Add(new User("Carla", "Recepción", "recepcion@hotel.com",
                _passwordHasher.Hash(_options.AdminPassword), "30111222", "3511112222", Role.Administrator));
            var ana = _userRepository.Add(new User("Ana", "Gómez", "ana@mail.com", clientHash, "40111222", "3515550001", Role.Client));
            var juan = _userRepository.Add(new User("Juan", "Pérez", "juan@mail.com", clientHash, "38222333", "3515550002", Role.Client));
            var maria = _userRepository.Add(new User("María", "López", "maria@mail.com", clientHash, "42333444", "3515550003", Role.Client));

            var room101 = _roomRepository.Add(new Room(101, RoomType.Single, 45000m, true, 1));
            _roomRepository.Add(new Room(102, RoomType.Single, 45000m, true, 1));
            var room201 = _roomRepository.Add(new Room(201, RoomType.Double, 70000m, true, 2));
            var room202 = _roomRepository.Add(new Room(202, RoomType.Double, 70000m, true, 2));
            var room301 = _roomRepository.Add(new Room(301, RoomType.Suite, 120000m, true, 4));
            var room401 = _roomRepository.Add(new Room(401, RoomType.Deluxe, 160000m, true, 3));
            _roomRepository.Add(new Room(501, RoomType.Double, 70000m, false, 2));   // fuera de servicio

            _unitOfWork.SaveChanges();

            // 2. Reservas (AddRoom valida capacidad, habitación habilitada y calcula el total)
            var anaReservation = new Reservation(NewCode(), new DateTime(2027, 1, 10), new DateTime(2027, 1, 15), ana.Id);
            anaReservation.AddRoom(room201, 2);                     // 5 noches x 70.000 = 350.000

            var juanReservation = new Reservation(NewCode(), new DateTime(2027, 2, 1), new DateTime(2027, 2, 4), juan.Id);
            juanReservation.AddRoom(room301, 4);
            juanReservation.AddRoom(room101, 1);                    // 3 noches x (120.000 + 45.000) = 495.000

            var mariaReservation = new Reservation(NewCode(), new DateTime(2027, 1, 20), new DateTime(2027, 1, 22), maria.Id);
            mariaReservation.AddRoom(room401, 2);                   // 2 noches x 160.000 = 320.000

            var anaCanceled = new Reservation(NewCode(), new DateTime(2027, 3, 5), new DateTime(2027, 3, 8), ana.Id);
            anaCanceled.AddRoom(room202, 1);
            anaCanceled.CancelReservation();

            _reservationRepository.Add(anaReservation);
            _reservationRepository.Add(juanReservation);
            _reservationRepository.Add(mariaReservation);
            _reservationRepository.Add(anaCanceled);

            _unitOfWork.SaveChanges();

            // 3. Pagos (nacen sin acreditar; se acreditan con el método del dominio)
            int transactionNumber = _paymentRepository.GetLastTransactionNumber();

            var anaPayment = new Payment(++transactionNumber, 175000m, "Transferencia", anaReservation.Id);
            anaPayment.ConfirmPayment();
            anaReservation.ConfirmReservation();                    // 175.000 = 50 % de 350.000

            var juanPayment = new Payment(++transactionNumber, 100000m, "Tarjeta de crédito", juanReservation.Id);
            juanPayment.ConfirmPayment();                           // 100.000 < 247.500: sigue pendiente

            _paymentRepository.Add(anaPayment);
            _paymentRepository.Add(juanPayment);

            _unitOfWork.SaveChanges();
            transaction.Commit();

            _logger.LogInformation("Datos de demo cargados: 4 usuarios, 7 habitaciones, 4 reservas y 2 pagos");
        }
        catch (Exception ex)
        {
            // Si la carga falla, la API arranca igual (con la base sin datos de demo).
            transaction.Rollback();
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "No se pudieron cargar los datos de demo");
        }
    }

    // Mismo formato que ReservationService: "RSV-" + 6 caracteres.
    private static string NewCode() => "RSV-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
}
