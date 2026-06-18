namespace OrderManagement.Application.DTOs.Orders;

/// <summary>
/// Lightweight read-only projection of an order's ownership fields.
/// Used by the application service layer to perform authorization checks
/// (e.g. "buyer can only cancel their own order") BEFORE entering the
/// transactional mutation path in the repository.
///
/// This is intentionally minimal (no items, no status history) to keep the
/// auth-check query cheap. The customer_id field is immutable after order
/// creation, so the TOCTOU window between this read and the subsequent
/// mutation is harmless — at worst the mutation fails with 404/409 if the
/// order was concurrently deleted or its row_version changed.
/// </summary>
public sealed record OrderOwnershipResult
{
    public required Guid Id { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Status { get; init; }

    public required long RowVersion { get; init; }

    public required string OrderNumber { get; init; }
}
