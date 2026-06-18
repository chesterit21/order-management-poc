namespace OrderManagement.Api.Contracts.Orders;

public sealed record OrderListQuery
{
    public string? Status { get; init; }

    public Guid? CustomerId { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}