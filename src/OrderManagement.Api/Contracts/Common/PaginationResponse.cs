namespace OrderManagement.Api.Contracts.Common;

public sealed record PaginationResponse
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public long TotalItems { get; init; }

    public int TotalPages { get; init; }
}