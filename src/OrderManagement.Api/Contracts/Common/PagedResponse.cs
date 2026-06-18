namespace OrderManagement.Api.Contracts.Common;

public sealed record PagedResponse<T>
{
    public IReadOnlyCollection<T> Items { get; init; } = [];

    public PaginationResponse Pagination { get; init; } = new();
}