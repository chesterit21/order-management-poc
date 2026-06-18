namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed record BackofficeOrderAccessDto
{
    public required Guid OrderId { get; init; }

    public required Guid StoreId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Status { get; init; }

    public required long RowVersion { get; init; }
}