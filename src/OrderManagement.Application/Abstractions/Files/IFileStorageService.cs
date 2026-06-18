using OrderManagement.Application.DTOs.Files;

namespace OrderManagement.Application.Abstractions.Files;

public interface IFileStorageService
{
    Task<StoredFileResult> SaveProductImageAsync(
        Guid productId,
        string originalFileName,
        string contentType,
        Stream content,
        long sizeBytes,
        CancellationToken cancellationToken = default);
}