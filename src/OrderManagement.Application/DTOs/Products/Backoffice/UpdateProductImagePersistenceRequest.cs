namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record UpdateProductImagePersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required string ImageUrl { get; init; }

    public required DateTimeOffset Now { get; init; }
}