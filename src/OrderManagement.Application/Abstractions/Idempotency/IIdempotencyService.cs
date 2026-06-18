using OrderManagement.Application.DTOs.Idempotency;

namespace OrderManagement.Application.Abstractions.Idempotency;

public interface IIdempotencyService
{
    Task<IdempotencyProcessResult> BeginAsync(
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        CancellationToken cancellationToken = default);
}