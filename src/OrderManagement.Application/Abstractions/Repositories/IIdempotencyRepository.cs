using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IIdempotencyRepository
{
    Task<bool> TryInsertInProgressAsync(
        CreateIdempotencyRecordRequest request,
        CancellationToken cancellationToken = default);

    Task<IdempotencyRecord?> GetByKeyAsync(
        Guid userId,
        string key,
        string endpoint,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}