namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record UploadProductImageCommand
{
    public required Guid ProductId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required Stream Content { get; init; }
}