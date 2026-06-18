using OrderManagement.Application.DTOs.Payments;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IPaymentRepository
{
    Task<CreatePaymentResult> CreateAsync(
        CreatePaymentPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentListResult> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}