using Application.Interfaces;
using Application.Models;
using Application.Models.Requests;
using Domain;
using Domain.Exceptions;
using Domain.Interfaces;

namespace Application.Services;

// Reglas de negocio de pagos (docx + definición del equipo):
// - Todo pago pertenece a una reserva existente (composición Reservation 1..* Payment).
// - Una reserva admite varios pagos parciales (seña, transferencia, saldo en el hotel...), cada uno con su método.
// - La suma de los pagos registrados nunca puede superar el TotalAmount de la reserva.
// - No se registran ni se confirman pagos de una reserva cancelada.
// - El pago nace con PaymentState = false y pasa a true solo al confirmarse (acreditarse).
// - Un pago confirmado no se puede eliminar.
// - Seña: cuando lo ACREDITADO llega al 50% del TotalAmount, la reserva pendiente pasa a Confirmed
//   y la habitación queda reservada. Los pagos sin acreditar no cuentan para la seña.
// Permisos: un cliente solo ve y registra pagos de sus propias reservas. Acreditar y
// eliminar pagos es tarea del staff (lo controla [Authorize] en PaymentController).
public class PaymentService : IPaymentService
{
    // Porcentaje del total que hay que tener acreditado para que la reserva quede confirmada.
    private const decimal DepositPercentage = 0.5m;

    private readonly IPaymentRepository _paymentRepository;
    private readonly IReservationRepository _reservationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IReservationRepository reservationRepository,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _reservationRepository = reservationRepository;
        _unitOfWork = unitOfWork;
    }

    // Obtiene todos los pagos registrados.
    public List<PaymentDto> GetAllPayments()
    {
        var payments = _paymentRepository.List();
        return PaymentDto.CreateList(payments);
    }

    // Obtiene un pago por su identificador.
    public PaymentDto GetPaymentById(int id, CurrentUser currentUser)
    {
        var payment = _paymentRepository.GetById(id)
            ?? throw new NotFoundException("PAYMENT_NOT_FOUND");

        var reservation = GetExistingReservation(payment.ReservationId);
        currentUser.EnsureCanAccess(reservation.UserId);

        return PaymentDto.Create(payment);
    }

    // Obtiene los pagos de una reserva.
    public List<PaymentDto> GetPaymentsByReservation(int reservationId, CurrentUser currentUser)
    {
        var reservation = GetExistingReservation(reservationId);
        currentUser.EnsureCanAccess(reservation.UserId);

        var payments = _paymentRepository.GetByReservation(reservationId);
        return PaymentDto.CreateList(payments);
    }

    // Registra un pago parcial (seña, saldo, pago en destino...) para una reserva.
    public PaymentDto RegisterPayment(CreatePaymentRequest request, CurrentUser currentUser)
    {
        var reservation = GetExistingReservation(request.ReservationId);
        currentUser.EnsureCanAccess(reservation.UserId);

        if (reservation.Type == ReservationState.Canceled)
            throw new AppValidationException("CANNOT_PAY_CANCELED_RESERVATION");

        // Los pagos pendientes también cuentan: representan dinero ya comprometido
        // (por ejemplo, el saldo que se abonará al llegar al hotel).
        decimal alreadyRegistered = _paymentRepository
            .GetByReservation(reservation.Id)
            .Sum(p => p.Amount);

        if (alreadyRegistered + request.Amount > reservation.TotalAmount)
            throw new AppValidationException("PAYMENT_EXCEEDS_RESERVATION_BALANCE");

        int transactionNumber = _paymentRepository.GetLastTransactionNumber() + 1;

        // El constructor valida monto > 0 y método obligatorio, y deja el pago sin acreditar.
        var payment = new Payment(
            transactionNumber,
            request.Amount,
            request.PaymentMethod,
            reservation.Id
        );

        _paymentRepository.Add(payment);
        _unitOfWork.SaveChanges();

        return PaymentDto.Create(payment);
    }

    // Acredita un pago (por ejemplo, cuando el huésped paga el saldo en el hotel).
    public PaymentDto ConfirmPayment(int id)
    {
        var payment = _paymentRepository.GetById(id)
            ?? throw new NotFoundException("PAYMENT_NOT_FOUND");

        var reservation = GetExistingReservation(payment.ReservationId);

        if (reservation.Type == ReservationState.Canceled)
            throw new AppValidationException("CANNOT_CONFIRM_PAYMENT_OF_CANCELED_RESERVATION");

        payment.ConfirmPayment();

        // Seña del 50%: lo acreditado hasta ahora (los otros pagos acreditados + este)
        // decide si la reserva pendiente queda confirmada.
        decimal confirmedAmount = _paymentRepository
            .GetByReservation(reservation.Id)
            .Where(p => p.PaymentState && p.Id != payment.Id)
            .Sum(p => p.Amount) + payment.Amount;

        if (reservation.Type == ReservationState.Pending && confirmedAmount >= GetRequiredDeposit(reservation))
            reservation.ConfirmReservation();

        _paymentRepository.Update(payment);
        _reservationRepository.Update(reservation);

        // Límite de la transacción: el pago acreditado y la reserva confirmada se guardan juntos,
        // en un único SaveChanges. Nunca queda la seña acreditada con la reserva sin confirmar.
        _unitOfWork.SaveChanges();

        return PaymentDto.Create(payment);
    }

    // Elimina un pago registrado por error. Solo se permite si todavía no fue acreditado.
    public void DeletePayment(int id)
    {
        var payment = _paymentRepository.GetById(id)
            ?? throw new NotFoundException("PAYMENT_NOT_FOUND");

        payment.EnsureCanBeDeleted();
        _paymentRepository.Delete(payment);
        _unitOfWork.SaveChanges();
    }

    private static decimal GetRequiredDeposit(Reservation reservation)
    {
        return Math.Round(reservation.TotalAmount * DepositPercentage, 2);
    }

    private Reservation GetExistingReservation(int reservationId)
    {
        return _reservationRepository.GetById(reservationId)
            ?? throw new NotFoundException("RESERVATION_NOT_FOUND");
    }
}
