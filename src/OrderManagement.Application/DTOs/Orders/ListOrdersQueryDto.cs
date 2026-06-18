namespace OrderManagement.Application.DTOs.Orders;

public sealed record ListOrdersQueryDto
{
    public string? Status { get; init; }

    public Guid? CustomerId { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}