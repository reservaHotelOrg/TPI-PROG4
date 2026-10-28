namespace Domain.Interfaces;

// El CRUD ya viene del genérico, acá solo lo propio de reservas.
public interface IReservationRepository : IRepository<Reservation>
{
    IEnumerable<Reservation> GetByUserId(int userId);

    // true si la habitación está libre entre esas fechas (ninguna reserva sin cancelar la tapa).
    bool IsRoomAvailable(int roomId, DateTime start, DateTime end);
}
