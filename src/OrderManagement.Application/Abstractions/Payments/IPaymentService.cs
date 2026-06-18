using OrderManagement.Application.DTOs.Payments;

namespace OrderManagement.Application.Abstractions.Payments;

public interface IPaymentService
{
    Task<CreatePaymentResult> CreateAsync(
        CreatePaymentCommand command,
        CancellationToken cancellationToken = default);

    Task<PaymentListResult> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}