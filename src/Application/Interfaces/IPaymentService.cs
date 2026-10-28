using Application.Models;
using Application.Models.Requests;

namespace Application.Interfaces;

public interface IPaymentService
{
    List<PaymentDto> GetAllPayments();
    PaymentDto GetPaymentById(int id, CurrentUser currentUser);
    List<PaymentDto> GetPaymentsByReservation(int reservationId, CurrentUser currentUser);
    PaymentDto RegisterPayment(CreatePaymentRequest request, CurrentUser currentUser);
    PaymentDto ConfirmPayment(int id);
    void DeletePayment(int id);
}
