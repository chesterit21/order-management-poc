namespace OrderManagement.Api.Contracts.Products;

public sealed record ProductListQuery
{
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}