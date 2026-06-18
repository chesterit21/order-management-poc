namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed record UploadProductImageResponse
{
    public Guid ProductId { get; init; }

    public Guid StoreId { get; init; }

    public string ImageUrl { get; init; } = string.Empty;

    public long RowVersion { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}