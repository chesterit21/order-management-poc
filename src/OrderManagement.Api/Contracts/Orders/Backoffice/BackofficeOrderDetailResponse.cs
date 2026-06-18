namespace OrderManagement.Api.Contracts.Orders.Backoffice;

public sealed record BackofficeOrderDetailResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public Guid StoreId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string ShippingAddress { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public long RowVersion { get; init; }

    public IReadOnlyCollection<BackofficeOrderItemResponse> Items { get; init; } = [];

    public IReadOnlyCollection<BackofficeOrderStatusHistoryResponse> StatusHistory { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record BackofficeOrderItemResponse
{
    public Guid ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineTotal { get; init; }
}

public sealed record BackofficeOrderStatusHistoryResponse
{
    public string? FromStatus { get; init; }

    public string ToStatus { get; init; } = string.Empty;

    public string? Reason { get; init; }

    public Guid ChangedBy { get; init; }

    public DateTimeOffset ChangedAt { get; init; }
}