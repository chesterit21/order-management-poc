namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed record BackofficeOrderDetailDto
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public required string Status { get; init; }

    public required string ShippingAddress { get; init; }

    public required decimal TotalAmount { get; init; }

    public required long RowVersion { get; init; }

    public required IReadOnlyCollection<BackofficeOrderItemDto> Items { get; init; }

    public required IReadOnlyCollection<BackofficeOrderStatusHistoryDto> StatusHistory { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record BackofficeOrderItemDto
{
    public required Guid ProductId { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}

public sealed record BackofficeOrderStatusHistoryDto
{
    public string? FromStatus { get; init; }

    public required string ToStatus { get; init; }

    public string? Reason { get; init; }

    public required Guid ChangedBy { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }
}