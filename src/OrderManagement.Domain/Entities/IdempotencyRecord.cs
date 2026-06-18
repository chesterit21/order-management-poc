using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public sealed class IdempotencyRecord : AuditableEntity
{
    private IdempotencyRecord()
    {
    }

    private IdempotencyRecord(
        Guid id,
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        IdempotencyStatus status,
        int? responseStatusCode,
        string? responseBody,
        string? resourceType,
        Guid? resourceId,
        DateTimeOffset? lockedUntil,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(key));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));
        }

        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new ArgumentException("Request hash is required.", nameof(requestHash));
        }

        Key = key.Trim();
        UserId = userId;
        Endpoint = endpoint.Trim();
        RequestHash = requestHash;
        Status = status;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        ResourceType = resourceType;
        ResourceId = resourceId;
        LockedUntil = lockedUntil;
        SetCreatedAt(createdAt);
    }

    public string Key { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public string Endpoint { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public IdempotencyStatus Status { get; private set; }

    public int? ResponseStatusCode { get; private set; }

    public string? ResponseBody { get; private set; }

    public string? ResourceType { get; private set; }

    public Guid? ResourceId { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public static IdempotencyRecord CreateInProgress(
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        DateTimeOffset lockedUntil,
        DateTimeOffset now)
    {
        return new IdempotencyRecord(
            Guid.NewGuid(),
            key,
            userId,
            endpoint,
            requestHash,
            IdempotencyStatus.InProgress,
            null,
            null,
            null,
            null,
            lockedUntil,
            now);
    }

    public static IdempotencyRecord Rehydrate(
        Guid id,
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        IdempotencyStatus status,
        int? responseStatusCode,
        string? responseBody,
        string? resourceType,
        Guid? resourceId,
        DateTimeOffset? lockedUntil,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var record = new IdempotencyRecord(
            id,
            key,
            userId,
            endpoint,
            requestHash,
            status,
            responseStatusCode,
            responseBody,
            resourceType,
            resourceId,
            lockedUntil,
            createdAt);

        record.SetUpdatedAt(updatedAt);

        return record;
    }

    public bool HasDifferentRequestHash(string requestHash)
    {
        return !string.Equals(RequestHash, requestHash, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsInProgress(DateTimeOffset now)
    {
        if (Status != IdempotencyStatus.InProgress)
        {
            return false;
        }

        return LockedUntil is null || LockedUntil > now;
    }

    public void MarkCompleted(
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        DateTimeOffset now)
    {
        Status = IdempotencyStatus.Completed;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        ResourceType = resourceType;
        ResourceId = resourceId;
        LockedUntil = null;
        SetUpdatedAt(now);
    }

    public void MarkFailed(
        int responseStatusCode,
        string responseBody,
        DateTimeOffset now)
    {
        Status = IdempotencyStatus.Failed;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        LockedUntil = null;
        SetUpdatedAt(now);
    }
}