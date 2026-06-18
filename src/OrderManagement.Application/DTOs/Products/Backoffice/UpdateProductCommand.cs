namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record UpdateProductCommand
{
    public required Guid ProductId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal Price { get; init; }

    public required long ExpectedRowVersion { get; init; }
}