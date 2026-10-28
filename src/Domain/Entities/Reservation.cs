using Domain.Exceptions;

namespace Domain;

// Una reserva es la estadía de un cliente: fechas, monto total y código.
// Arranca pendiente y después se confirma o se cancela.
// Las propiedades tienen "private set" y las colecciones son de solo lectura: el estado
// solo cambia por los métodos de negocio (AddRoom, ConfirmReservation,
// CancelReservation), que son los que validan las reglas.
public class Reservation
{
    private readonly List<DetailReservation> _detailReservations = new();
    private readonly List<Payment> _payments = new();

    public int Id { get; private set; }
    public string ReservationCode { get; private set; } = string.Empty;
    public DateTime CreationDate { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public ReservationState Type { get; private set; }
    public decimal TotalAmount { get; private set; }

    public int UserId { get; private set; }
    public User User { get; private set; } = null!;
    public IReadOnlyCollection<DetailReservation> DetailReservations => _detailReservations;  // 1..*
    public IReadOnlyCollection<Payment> Payments => _payments;                                // 1..*

    // Lo pide Entity Framework (privado: solo lo usa EF al leer de la base).
    private Reservation() { }

    // El id lo pone la base, el código lo genera el servicio y la fecha la pone el servidor.
    // El total no entra por acá: lo calcula el sistema con las habitaciones (nunca lo manda el cliente).
    public Reservation(string reservationCode, DateTime startDate, DateTime endDate, int userId)
    {
        if (string.IsNullOrWhiteSpace(reservationCode))
            throw new AppValidationException("RESERVATION_CODE_REQUIRED");

        if (startDate >= endDate)
            throw new AppValidationException("INVALID_RESERVATION_DATES");

        ReservationCode = reservationCode.Trim();
        StartDate = startDate;
        EndDate = endDate;
        UserId = userId;
        CreationDate = DateTime.Now;
        Type = ReservationState.Pending;
    }

    // Agrega una habitación a la reserva y recalcula el total.
    // La disponibilidad en las fechas (overbooking) la controla el servicio con el repositorio,
    // porque necesita mirar las otras reservas guardadas en la base.
    public void AddRoom(Room room, int guestsNumber)
    {
        if (Type != ReservationState.Pending)
            throw new AppValidationException("RESERVATION_NOT_EDITABLE");

        if (!room.State)
            throw new AppValidationException("ROOM_OUT_OF_SERVICE");

        if (_detailReservations.Any(detail => detail.RoomId == room.Id))
            throw new AppValidationException("DUPLICATED_ROOM_IN_RESERVATION");

        // El constructor del detalle valida la capacidad.
        _detailReservations.Add(new DetailReservation(room, guestsNumber));

        TotalAmount = CalculateTotal(StartDate, EndDate);
    }

    // Pasa la reserva a confirmada (cuando se acredita la seña, por ejemplo).
    public void ConfirmReservation()
    {
        if (Type == ReservationState.Confirmed)
            throw new AppValidationException("RESERVATION_ALREADY_CONFIRMED");

        if (Type == ReservationState.Canceled)
            throw new AppValidationException("CANNOT_CONFIRM_CANCELED_RESERVATION");

        Type = ReservationState.Confirmed;
    }

    // Pasa la reserva a cancelada, después ya no se puede confirmar ni cobrar.
    public void CancelReservation()
    {
        if (Type == ReservationState.Canceled)
            throw new AppValidationException("RESERVATION_ALREADY_CANCELED");

        Type = ReservationState.Canceled;
    }

    // Con pagos registrados la reserva es historial contable: se cancela, no se borra.
    public void EnsureCanBeDeleted()
    {
        if (_payments.Any())
            throw new AppValidationException("RESERVATION_HAS_PAYMENTS");
    }

    // Regla "Independencia de tarifa": noches x precio por noche de cada habitación,
    // sin importar la cantidad de huéspedes.
    private decimal CalculateTotal(DateTime startDate, DateTime endDate)
    {
        // La reserva no existe sin por lo menos una habitación (composición 1..*): sin detalles no hay total.
        if (_detailReservations.Count == 0)
            throw new AppValidationException("RESERVATION_MUST_HAVE_ROOMS");

        int nights = (endDate.Date - startDate.Date).Days;

        return _detailReservations.Sum(detail => nights * detail.Room.PricePerNight);
    }
}
