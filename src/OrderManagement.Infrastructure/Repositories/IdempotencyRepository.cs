using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class IdempotencyRepository : IIdempotencyRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public IdempotencyRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> TryInsertInProgressAsync(
        CreateIdempotencyRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           INSERT INTO idempotency_keys
                               (key, user_id, endpoint, request_hash, status, locked_until, created_at, updated_at)
                           VALUES
                               (@Key, @UserId, @Endpoint, @RequestHash, @Status, @LockedUntil, @Now, @Now)
                           ON CONFLICT (user_id, key, endpoint) DO NOTHING;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    request.Key,
                    request.UserId,
                    request.Endpoint,
                    request.RequestHash,
                    Status = IdempotencyStatus.InProgress.ToString(),
                    request.LockedUntil,
                    request.Now
                },
                cancellationToken: cancellationToken));

        return affectedRows == 1;
    }

    public async Task<IdempotencyRecord?> GetByKeyAsync(
        Guid userId,
        string key,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               key AS Key,
                               user_id AS UserId,
                               endpoint AS Endpoint,
                               request_hash AS RequestHash,
                               status AS Status,
                               response_status_code AS ResponseStatusCode,
                               response_body::text AS ResponseBody,
                               resource_type AS ResourceType,
                               resource_id AS ResourceId,
                               locked_until AS LockedUntil,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM idempotency_keys
                           WHERE user_id = @UserId
                             AND key = @Key
                             AND endpoint = @Endpoint
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRecordRow>(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = userId,
                    Key = key.Trim(),
                    Endpoint = endpoint.Trim()
                },
                cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    public async Task MarkCompletedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE idempotency_keys
                           SET
                               status = @Status,
                               response_status_code = @ResponseStatusCode,
                               response_body = CAST(@ResponseBody AS jsonb),
                               resource_type = @ResourceType,
                               resource_id = @ResourceId,
                               locked_until = NULL,
                               updated_at = @Now
                           WHERE id = @RecordId
                             AND status = @ExpectedStatus;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    RecordId = recordId,
                    Status = IdempotencyStatus.Completed.ToString(),
                    ExpectedStatus = IdempotencyStatus.InProgress.ToString(),
                    ResponseStatusCode = responseStatusCode,
                    ResponseBody = responseBody,
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    Now = now
                },
                cancellationToken: cancellationToken));

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Failed to mark idempotency record '{recordId}' as completed. Record may not be in InProgress state.");
        }
    }

    public async Task MarkFailedAsync(
        Guid recordId,
        int responseStatusCode,
        string responseBody,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE idempotency_keys
                           SET
                               status = @Status,
                               response_status_code = @ResponseStatusCode,
                               response_body = CAST(@ResponseBody AS jsonb),
                               locked_until = NULL,
                               updated_at = @Now
                           WHERE id = @RecordId
                             AND status = @ExpectedStatus;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    RecordId = recordId,
                    Status = IdempotencyStatus.Failed.ToString(),
                    ExpectedStatus = IdempotencyStatus.InProgress.ToString(),
                    ResponseStatusCode = responseStatusCode,
                    ResponseBody = responseBody,
                    Now = now
                },
                cancellationToken: cancellationToken));

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Failed to mark idempotency record '{recordId}' as failed. Record may not be in InProgress state.");
        }
    }

    private sealed class IdempotencyRecordRow
    {
        public Guid Id { get; init; }

        public string Key { get; init; } = string.Empty;

        public Guid UserId { get; init; }

        public string Endpoint { get; init; } = string.Empty;

        public string RequestHash { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public int? ResponseStatusCode { get; init; }

        public string? ResponseBody { get; init; }

        public string? ResourceType { get; init; }

        public Guid? ResourceId { get; init; }

        public DateTimeOffset? LockedUntil { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public IdempotencyRecord ToDomain()
        {
            if (!Enum.TryParse<IdempotencyStatus>(Status, ignoreCase: true, out var parsedStatus))
            {
                throw new InvalidOperationException($"Invalid idempotency status value '{Status}' in database.");
            }

            return IdempotencyRecord.Rehydrate(
                Id,
                Key,
                UserId,
                Endpoint,
                RequestHash,
                parsedStatus,
                ResponseStatusCode,
                ResponseBody,
                ResourceType,
                ResourceId,
                LockedUntil,
                CreatedAt,
                UpdatedAt);
        }
    }
}