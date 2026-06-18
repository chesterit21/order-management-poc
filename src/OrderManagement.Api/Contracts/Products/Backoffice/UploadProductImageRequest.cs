namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed record UploadProductImageRequest
{
    public IFormFile File { get; init; } = null!;
}
