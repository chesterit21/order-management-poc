namespace OrderManagement.Infrastructure.Options;

public sealed class FileUploadOptions
{
    public const string SectionName = "FileUpload";

    public long MaxProductImageSizeBytes { get; init; } = 2 * 1024 * 1024;

    public string ProductImageRootPath { get; init; } = "wwwroot/uploads/products";

    public string ProductImagePublicBasePath { get; init; } = "/uploads/products";

    public string[] AllowedProductImageExtensions { get; init; } =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    ];

    public string[] AllowedProductImageContentTypes { get; init; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];
}