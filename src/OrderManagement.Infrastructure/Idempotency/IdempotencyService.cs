using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Idempotency;

public sealed class IdempotencyService(
    IIdempotencyRepository repository,
    IClock clock,
    ILogger<IdempotencyService> logger,
    IActivityLogWriter activityLogWriter) : IIdempotencyService
{
    private readonly IIdempotencyRepository _repository = repository;
    private readonly IClock _clock = clock;
    private readonly ILogger<IdempotencyService> _logger = logger;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;

    public async Task<IdempotencyProcessResult> BeginAsync(
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var lockedUntil = now.AddMinutes(5);

        // Try to insert a new in-progress record
        var inserted = await _repository.TryInsertInProgressAsync(
            new CreateIdempotencyRecordRequest
            {
                Key = key.Trim(),
                UserId = userId,
                Endpoint = endpoint.Trim(),
                RequestHash = requestHash,
                LockedUntil = lockedUntil,
                Now = now
            },
            cancellationToken);

        if (inserted)
        {
            // Need to get the record ID - query by key
            var record = await _repository.GetByKeyAsync(userId, key.Trim(), endpoint.Trim(), cancellationToken);

            if (record is null)
            {
                throw new InvalidOperationException("Failed to retrieve newly created idempotency record.");
            }

            _logger.LogInformation(
                "Idempotency key accepted for processing. Key={Key} RecordId={RecordId}",
                MaskKey(key),
                record.Id);

            _activityLogWriter.TryWrite(
                ActivityLogTypes.IdempotencyAccepted,
                metadata: new
                {
                    endpoint,
                    idempotencyKeyPrefix = MaskKey(key),
                    recordId = record.Id
                });

            return IdempotencyProcessResult.ProcessRequest(record.Id);
        }

        // Record already exists - fetch it
        var existing = await _repository.GetByKeyAsync(userId, key.Trim(), endpoint.Trim(), cancellationToken);

        if (existing is null)
        {
            throw new InvalidOperationException("Idempotency conflict but no existing record found.");
        }

        // Check if it has a different request hash (conflict)
        if (existing.HasDifferentRequestHash(requestHash))
        {
            _activityLogWriter.TryWrite(
                ActivityLogTypes.IdempotencyConflict,
                errorCode: "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD",
                metadata: new
                {
                    endpoint,
                    idempotencyKeyPrefix = MaskKey(key),
                    reason = "DifferentPayload"
                });

            throw new IdempotencyConflictException(
                $"Idempotency key '{MaskKey(key)}' was already used with a different request payload.",
                IdempotencyConflictType.DifferentPayload,
                existing);
        }

        // Check if still in progress
        if (existing.IsInProgress(now))
        {
            _activityLogWriter.TryWrite(
                ActivityLogTypes.IdempotencyConflict,
                errorCode: "REQUEST_ALREADY_IN_PROGRESS",
                metadata: new
                {
                    endpoint,
                    idempotencyKeyPrefix = MaskKey(key),
                    recordId = existing.Id,
                    reason = "InProgress"
                });

            throw new IdempotencyConflictException(
                $"A request with idempotency key '{MaskKey(key)}' is already being processed.",
                IdempotencyConflictType.InProgress,
                existing);
        }

        // Record is completed - return stored response
        _logger.LogInformation(
            "Idempotency key replay returned. Key={Key} RecordId={RecordId} ResponseStatusCode={ResponseStatusCode}",
            MaskKey(key),
            existing.Id,
            existing.ResponseStatusCode);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.IdempotencyReplayReturned,
            statusCode: existing.ResponseStatusCode,
            metadata: new
            {
                endpoint,
                idempotencyKeyPrefix = MaskKey(key),
                recordId = existing.Id,
                resourceType = existing.ResourceType,
                resourceId = existing.ResourceId
            });

        return IdempotencyProcessResult.ReturnStoredResponse(
            existing.ResponseStatusCode ?? 200,
            existing.ResponseBody ?? string.Empty);
    }

    public async Task MarkCompletedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        await _repository.MarkCompletedAsync(
            recordId,
            responseStatusCode,
            responseBody,
            resourceType,
            resourceId,
            now,
            cancellationToken);

        _logger.LogInformation(
            "Idempotency record completed. RecordId={RecordId} StatusCode={StatusCode} ResourceType={ResourceType} ResourceId={ResourceId}",
            recordId,
            responseStatusCode,
            resourceType,
            resourceId);
    }

    public async Task MarkFailedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        await _repository.MarkFailedAsync(
            recordId,
            responseStatusCode,
            responseBody,
            now,
            cancellationToken);

        _logger.LogWarning(
            "Idempotency record failed. RecordId={RecordId} StatusCode={StatusCode}",
            recordId,
            responseStatusCode);
    }

    private static string MaskKey(string key) =>
        key.Length > 8 ? key[..8] + "..." : key;
}
