namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed record UpdateProductRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public decimal Price { get; init; }

    public long ExpectedRowVersion { get; init; }
}