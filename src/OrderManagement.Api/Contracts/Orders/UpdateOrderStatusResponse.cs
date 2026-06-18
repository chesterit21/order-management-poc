namespace OrderManagement.Api.Contracts.Orders;

public sealed record UpdateOrderStatusResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public string PreviousStatus { get; init; } = string.Empty;

    public string CurrentStatus { get; init; } = string.Empty;

    public long RowVersion { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}