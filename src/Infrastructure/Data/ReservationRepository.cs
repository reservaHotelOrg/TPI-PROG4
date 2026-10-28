using Domain;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

// Lo básico (Add, Update, Delete) ya viene de Repository. GetById y List acá
// se sobrescriben para que cada reserva traiga sus habitaciones (con la Room cargada)
// y sus pagos, porque casi todo el negocio los necesita.
// Si no encuentra nada devuelve null y el service decide qué hacer con eso.
public class ReservationRepository : Repository<Reservation>, IReservationRepository
{
    public ReservationRepository(ApplicationContext context) : base(context) { }

    // La reserva con todo lo que le cuelga, en una sola ida a la base.
    public override Reservation? GetById(int id)
    {
        return _dbSet
            .Include(reservation => reservation.DetailReservations)
                .ThenInclude(detail => detail.Room)
            .Include(reservation => reservation.Payments)
            .FirstOrDefault(reservation => reservation.Id == id);
    }

    public override IEnumerable<Reservation> List()
    {
        return _dbSet
            .Include(reservation => reservation.DetailReservations)
                .ThenInclude(detail => detail.Room)
            .Include(reservation => reservation.Payments)
            .ToList();
    }

    // Trae las reservas de un cliente, de la más próxima a la más lejana.
    public IEnumerable<Reservation> GetByUserId(int userId)
    {
        return _dbSet
            .Include(reservation => reservation.DetailReservations)
                .ThenInclude(detail => detail.Room)
            .Include(reservation => reservation.Payments)
            .Where(reservation => reservation.UserId == userId)
            .OrderBy(reservation => reservation.StartDate)
            .ToList();
    }

    // Una habitación está ocupada si alguna reserva sin cancelar la usa en fechas que se pisan.
    // El día de check-out de una reserva puede ser el check-in de la siguiente (por eso el < y el >).
    public bool IsRoomAvailable(int roomId, DateTime start, DateTime end)
    {
        return !_context.DetailReservations
            .Include(detail => detail.Reservation)
            .Any(detail =>
                detail.RoomId == roomId
                && detail.Reservation.Type != ReservationState.Canceled
                && start < detail.Reservation.EndDate
                && end > detail.Reservation.StartDate);
    }
}
