using Application.Interfaces;
using Application.Models;
using Application.Models.Requests;
using Domain;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

// Todo lo de reservas pasa por acá: valida que el cliente exista, que las habitaciones
// existan, estén habilitadas, libres y con capacidad, y el total lo calcula el sistema.
// La reserva siempre nace pendiente y se crea junto con sus habitaciones.
// Permisos: un cliente solo ve y cancela sus propias reservas; el staff, todas.
public class ReservationService : IReservationService
{
    private readonly IReservationRepository _reservationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoomRepository _roomRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReservationService(
        IReservationRepository reservationRepository,
        IUserRepository userRepository,
        IRoomRepository roomRepository,
        IUnitOfWork unitOfWork)
    {
        _reservationRepository = reservationRepository;
        _userRepository = userRepository;
        _roomRepository = roomRepository;
        _unitOfWork = unitOfWork;
    }

    // Trae todas las reservas.
    public List<ReservationDto> GetAllReservations()
    {
        var reservations = _reservationRepository.List();
        return ReservationDto.CreateList(reservations);
    }

    // Trae una reserva por su id.
    public ReservationDto GetReservationById(int id, CurrentUser currentUser)
    {
        var reservation = _reservationRepository.GetById(id)
            ?? throw new NotFoundException("RESERVATION_NOT_FOUND");

        currentUser.EnsureCanAccess(reservation.UserId);

        return ReservationDto.Create(reservation);
    }

    // Trae las reservas de un cliente.
    public List<ReservationDto> GetReservationsByUser(int userId, CurrentUser currentUser)
    {
        currentUser.EnsureCanAccess(userId);
        GetExistingUser(userId);

        var reservations = _reservationRepository.GetByUserId(userId);
        return ReservationDto.CreateList(reservations);
    }

    // Crea la reserva y sus habitaciones en una sola operación, a nombre del usuario logueado.
    // El código lo genera el servidor, arranca pendiente y el total lo calcula el sistema
    // (nunca lo manda el cliente).
    public ReservationDto CreateReservation(CreateReservationRequest request, CurrentUser currentUser)
    {
        ValidateReservationDates(request.StartDate, request.EndDate);

        GetExistingUser(currentUser.Id);

        if (request.Rooms is null || request.Rooms.Count == 0)
            throw new AppValidationException("RESERVATION_MUST_HAVE_ROOMS");

        var reservation = new Reservation(
            "RSV-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
            request.StartDate,
            request.EndDate,
            currentUser.Id
        );

        foreach (var roomRequest in request.Rooms)
        {
            var room = _roomRepository.GetById(roomRequest.RoomId)
                ?? throw new NotFoundException("ROOM_NOT_FOUND");

            // Overbooking: mira las otras reservas guardadas, por eso lo controla el servicio.
            if (!_reservationRepository.IsRoomAvailable(room.Id, request.StartDate, request.EndDate))
                throw new AppValidationException("ROOM_NOT_AVAILABLE_FOR_DATES");

            // La entidad valida que la habitación esté habilitada, que no esté repetida y la
            // capacidad, y recalcula el total (regla "Independencia de tarifa").
            reservation.AddRoom(room, roomRequest.GuestsNumber);
        }

        // Un solo Add: la reserva le pasa sus detalles por la navegación y EF los inserta juntos.
        _reservationRepository.Add(reservation);
        _unitOfWork.SaveChanges();

        return ReservationDto.Create(reservation);
    }

    // Pasa la reserva a confirmada.
    public ReservationDto ConfirmReservation(int id)
    {
        var reservation = _reservationRepository.GetById(id)
            ?? throw new NotFoundException("RESERVATION_NOT_FOUND");

        reservation.ConfirmReservation();
        _reservationRepository.Update(reservation);
        _unitOfWork.SaveChanges();

        return ReservationDto.Create(reservation);
    }

    // Pasa la reserva a cancelada.
    public ReservationDto CancelReservation(int id, CurrentUser currentUser)
    {
        var reservation = _reservationRepository.GetById(id)
            ?? throw new NotFoundException("RESERVATION_NOT_FOUND");

        currentUser.EnsureCanAccess(reservation.UserId);

        reservation.CancelReservation();
        _reservationRepository.Update(reservation);
        _unitOfWork.SaveChanges();

        return ReservationDto.Create(reservation);
    }

    // Borra la reserva, pero solo si no tiene ningún pago registrado: con pagos el
    // historial contable queda y la salida es cancelar la reserva, no borrarla.
    public void DeleteReservation(int id)
    {
        var reservation = _reservationRepository.GetById(id)
            ?? throw new NotFoundException("RESERVATION_NOT_FOUND");

        reservation.EnsureCanBeDeleted();

        _reservationRepository.Delete(reservation);
        _unitOfWork.SaveChanges();
    }

    // Fechas del documento: la estadía empieza hoy o más tarde y termina después de empezar.
    // Si el cliente no manda la fecha de inicio, llega DateTime.MinValue y este check lo tira afuera.
    private static void ValidateReservationDates(DateTime startDate, DateTime endDate)
    {
        if (startDate < DateTime.Today)
            throw new AppValidationException("START_DATE_IN_THE_PAST");

        if (startDate >= endDate)
            throw new AppValidationException("INVALID_RESERVATION_DATES");
    }

    private User GetExistingUser(int userId)
    {
        return _userRepository.GetById(userId)
            ?? throw new NotFoundException("USER_NOT_FOUND");
    }
}
