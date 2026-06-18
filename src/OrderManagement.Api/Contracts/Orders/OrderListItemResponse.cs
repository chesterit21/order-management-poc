namespace OrderManagement.Api.Contracts.Orders;

public sealed record OrderListItemResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public long RowVersion { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}