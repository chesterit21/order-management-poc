namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record UploadProductImageResult
{
    public required Guid ProductId { get; init; }

    public required Guid StoreId { get; init; }

    public required string ImageUrl { get; init; }

    public required long RowVersion { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}