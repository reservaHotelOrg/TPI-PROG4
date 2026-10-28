using Application.Models;
using Application.Models.Requests;

namespace Application.Interfaces;

public interface IReservationService
{
    List<ReservationDto> GetAllReservations();
    ReservationDto GetReservationById(int id, CurrentUser currentUser);
    List<ReservationDto> GetReservationsByUser(int userId, CurrentUser currentUser);
    ReservationDto CreateReservation(CreateReservationRequest request, CurrentUser currentUser);
    ReservationDto ConfirmReservation(int id);
    ReservationDto CancelReservation(int id, CurrentUser currentUser);
    void DeleteReservation(int id);
}
