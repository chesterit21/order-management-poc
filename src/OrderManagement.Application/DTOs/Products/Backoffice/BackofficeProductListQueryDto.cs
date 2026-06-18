namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record BackofficeProductListQueryDto
{
    public Guid? StoreId { get; init; }

    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}