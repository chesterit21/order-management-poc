using OrderManagement.Application.DTOs.Demo;

namespace OrderManagement.Application.Abstractions.Demo;

public interface IDemoService
{
    /// <summary>
    /// Demonstrates concurrent stock deduction scenario (Skenario A):
    /// Two concurrent orders for the same product, each requesting the same quantity.
    /// Only one should succeed when stock is insufficient for both.
    /// Uses real Idempotency-Key generation + full OrderService pipeline + FOR UPDATE locking.
    /// </summary>
    Task<ConcurrentStockDeductionResponse> RunConcurrentStockDeductionAsync(
        ConcurrentStockDeductionRequest request,
        CancellationToken cancellationToken = default);
}
