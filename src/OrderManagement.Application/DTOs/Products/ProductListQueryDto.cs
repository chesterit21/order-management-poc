namespace OrderManagement.Application.DTOs.Products;

public sealed record ProductListQueryDto
{
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}