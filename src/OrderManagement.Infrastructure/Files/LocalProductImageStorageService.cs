using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.Files;
using OrderManagement.Application.DTOs.Files;
using OrderManagement.Application.Exceptions;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.Files;

public sealed class LocalProductImageStorageService : IFileStorageService
{
    private readonly FileUploadOptions _options;
    private readonly string _rootPath;

    public LocalProductImageStorageService(
        IOptions<FileUploadOptions> options,
        string productImageRootPath)
    {
        _options = options.Value;
        _rootPath = Path.GetFullPath(productImageRootPath);
    }

    public async Task<StoredFileResult> SaveProductImageAsync(
        Guid productId,
        string originalFileName,
        string contentType,
        Stream content,
        long sizeBytes,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product ID cannot be empty.");
        }

        // Validate file size
        if (sizeBytes > _options.MaxProductImageSizeBytes)
        {
            throw new ArgumentException($"File size exceeds maximum allowed size of {_options.MaxProductImageSizeBytes} bytes.");
        }

        // Validate file extension
        var fileExtension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!_options.AllowedProductImageExtensions.Contains(fileExtension))
        {
            throw new ArgumentException($"File extension '{fileExtension}' is not allowed. Allowed extensions: [{string.Join(", ", _options.AllowedProductImageExtensions)}].");
        }

        // Validate content type
        if (!_options.AllowedProductImageContentTypes.Contains(contentType))
        {
            throw new ArgumentException($"Content type '{contentType}' is not allowed. Allowed content types: [{string.Join(", ", _options.AllowedProductImageContentTypes)}].");
        }

        // Generate secure filename using GUID to prevent path traversal and ensure uniqueness
        var secureFileName = $"{Guid.NewGuid()}{fileExtension}";

        // Ensure the uploads directory exists
        Directory.CreateDirectory(_rootPath);

        // Construct full file path
        var filePath = Path.Combine(_rootPath, secureFileName);

        // Save the file
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await content.CopyToAsync(fileStream, cancellationToken);

        // Return the result with public URL
        var publicUrl = $"{_options.ProductImagePublicBasePath}/{secureFileName}";

        return new StoredFileResult
        {
            PublicUrl = publicUrl,
            StoredFileName = secureFileName,
            ContentType = contentType,
            SizeBytes = sizeBytes
        };
    }
}