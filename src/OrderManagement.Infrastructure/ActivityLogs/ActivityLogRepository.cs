using Dapper;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class ActivityLogRepository(IDbConnectionFactory connectionFactory) : IActivityLogRepository
{
    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;

    public async Task InsertBatchAsync(
        IReadOnlyCollection<ActivityLogMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var parameters = new DynamicParameters();
        var values = new List<string>(messages.Count);

        var index = 0;

        foreach (var message in messages)
        {
            var prefix = $"p{index}";

            values.Add($"""
                        (
                            @{prefix}Id,
                            @{prefix}CorrelationId,
                            @{prefix}ActivityType,
                            @{prefix}ActorUserId,
                            @{prefix}ActorUsername,
                            @{prefix}ActorRole,
                            @{prefix}OrderId,
                            @{prefix}OrderNumber,
                            @{prefix}ProductId,
                            @{prefix}PaymentId,
                            @{prefix}RequestPath,
                            @{prefix}HttpMethod,
                            @{prefix}StatusCode,
                            @{prefix}ElapsedMs,
                            @{prefix}ErrorCode,
                            CAST(@{prefix}BeforeStateJson AS jsonb),
                            CAST(@{prefix}AfterStateJson AS jsonb),
                            CAST(@{prefix}MetadataJson AS jsonb),
                            @{prefix}CreatedAt
                        )
                        """);

            parameters.Add($"{prefix}Id", message.Id);
            parameters.Add($"{prefix}CorrelationId", TrimToLength(message.CorrelationId, 100));
            parameters.Add($"{prefix}ActivityType", TrimToLength(message.ActivityType, 100));
            parameters.Add($"{prefix}ActorUserId", message.ActorUserId);
            parameters.Add($"{prefix}ActorUsername", TrimToLength(message.ActorUsername, 100));
            parameters.Add($"{prefix}ActorRole", TrimToLength(message.ActorRole, 50));
            parameters.Add($"{prefix}OrderId", message.OrderId);
            parameters.Add($"{prefix}OrderNumber", TrimToLength(message.OrderNumber, 50));
            parameters.Add($"{prefix}ProductId", message.ProductId);
            parameters.Add($"{prefix}PaymentId", message.PaymentId);
            parameters.Add($"{prefix}RequestPath", TrimToLength(message.RequestPath, 500));
            parameters.Add($"{prefix}HttpMethod", TrimToLength(message.HttpMethod, 20));
            parameters.Add($"{prefix}StatusCode", message.StatusCode);
            parameters.Add($"{prefix}ElapsedMs", message.ElapsedMs);
            parameters.Add($"{prefix}ErrorCode", TrimToLength(message.ErrorCode, 100));
            parameters.Add($"{prefix}BeforeStateJson", NormalizeJsonOrNull(message.BeforeStateJson));
            parameters.Add($"{prefix}AfterStateJson", NormalizeJsonOrNull(message.AfterStateJson));
            parameters.Add($"{prefix}MetadataJson", NormalizeJsonOrNull(message.MetadataJson));
            parameters.Add($"{prefix}CreatedAt", message.CreatedAt);

            index++;
        }

        var sql = $"""
                   INSERT INTO activity_logs
                       (
                           id,
                           correlation_id,
                           activity_type,
                           actor_user_id,
                           actor_username,
                           actor_role,
                           order_id,
                           order_number,
                           product_id,
                           payment_id,
                           request_path,
                           http_method,
                           status_code,
                           elapsed_ms,
                           error_code,
                           before_state,
                           after_state,
                           metadata,
                           created_at
                       )
                   VALUES
                       {string.Join(",\n", values)};
                   """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Lists activity log entries with optional filtering and pagination.
    /// Uses a parameterized WHERE clause built from the query DTO to prevent SQL injection.
    /// Results are ordered by created_at DESC (newest first).
    /// </summary>
    public async Task<PagedResult<ActivityLogListItemDto>> ListAsync(
        ActivityLogListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        // Normalize pagination
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 200);
        var offset = (page - 1) * pageSize;

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.ActivityType))
        {
            conditions.Add("activity_type = @ActivityType");
            parameters.Add("ActivityType", query.ActivityType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            conditions.Add("correlation_id = @CorrelationId");
            parameters.Add("CorrelationId", query.CorrelationId.Trim());
        }

        if (query.ActorUserId.HasValue)
        {
            conditions.Add("actor_user_id = @ActorUserId");
            parameters.Add("ActorUserId", query.ActorUserId.Value);
        }

        if (query.OrderId.HasValue)
        {
            conditions.Add("order_id = @OrderId");
            parameters.Add("OrderId", query.OrderId.Value);
        }

        if (query.ProductId.HasValue)
        {
            conditions.Add("product_id = @ProductId");
            parameters.Add("ProductId", query.ProductId.Value);
        }

        if (query.PaymentId.HasValue)
        {
            conditions.Add("payment_id = @PaymentId");
            parameters.Add("PaymentId", query.PaymentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.ErrorCode))
        {
            conditions.Add("error_code = @ErrorCode");
            parameters.Add("ErrorCode", query.ErrorCode.Trim());
        }

        if (query.MinStatusCode.HasValue)
        {
            conditions.Add("status_code >= @MinStatusCode");
            parameters.Add("MinStatusCode", query.MinStatusCode.Value);
        }

        if (query.FromDate.HasValue)
        {
            conditions.Add("created_at >= @FromDate");
            parameters.Add("FromDate", query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            conditions.Add("created_at <= @ToDate");
            parameters.Add("ToDate", query.ToDate.Value);
        }

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : string.Empty;

        parameters.Add("Limit", pageSize);
        parameters.Add("Offset", offset);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        // Query items and total count in a single round-trip using QueryMultipleAsync
        var sql = $"""
                   SELECT
                       id,
                       correlation_id AS CorrelationId,
                       activity_type AS ActivityType,
                       actor_user_id AS ActorUserId,
                       actor_username AS ActorUsername,
                       actor_role AS ActorRole,
                       order_id AS OrderId,
                       order_number AS OrderNumber,
                       product_id AS ProductId,
                       payment_id AS PaymentId,
                       request_path AS RequestPath,
                       http_method AS HttpMethod,
                       status_code AS StatusCode,
                       elapsed_ms AS ElapsedMs,
                       error_code AS ErrorCode,
                       before_state::text AS BeforeStateJson,
                       after_state::text AS AfterStateJson,
                       metadata::text AS MetadataJson,
                       created_at AS CreatedAt
                   FROM activity_logs
                   {whereClause}
                   ORDER BY created_at DESC
                   LIMIT @Limit OFFSET @Offset;

                   SELECT COUNT(*)
                   FROM activity_logs
                   {whereClause};
                   """;

        await using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<ActivityLogListItemDto>()).ToArray();
        var totalItems = await multi.ReadFirstAsync<long>();

        return new PagedResult<ActivityLogListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }

    /// <summary>
    /// Lists activity log entries (v2) with LIKE filtering on CorrelationId, OrderNumber, and ActivityType.
    /// Uses parameterized WHERE clause to prevent SQL injection.
    /// Results are ordered by created_at DESC (newest first).
    /// </summary>
    public async Task<PagedResult<ActivityLogListItemDto>> ListV2Async(
        ActivityLogListV2QueryDto query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 200);
        var offset = (page - 1) * pageSize;

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            conditions.Add("correlation_id LIKE @CorrelationId");
            parameters.Add("CorrelationId", $"%{query.CorrelationId.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(query.OrderNumber))
        {
            conditions.Add("order_number LIKE @OrderNumber");
            parameters.Add("OrderNumber", $"%{query.OrderNumber.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(query.ActivityType))
        {
            conditions.Add("activity_type LIKE @ActivityType");
            parameters.Add("ActivityType", $"%{query.ActivityType.Trim()}%");
        }

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : string.Empty;

        parameters.Add("Limit", pageSize);
        parameters.Add("Offset", offset);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var sql = $"""
                   SELECT
                       id,
                       correlation_id AS CorrelationId,
                       activity_type AS ActivityType,
                       actor_user_id AS ActorUserId,
                       actor_username AS ActorUsername,
                       actor_role AS ActorRole,
                       order_id AS OrderId,
                       order_number AS OrderNumber,
                       product_id AS ProductId,
                       payment_id AS PaymentId,
                       request_path AS RequestPath,
                       http_method AS HttpMethod,
                       status_code AS StatusCode,
                       elapsed_ms AS ElapsedMs,
                       error_code AS ErrorCode,
                       before_state::text AS BeforeStateJson,
                       after_state::text AS AfterStateJson,
                       metadata::text AS MetadataJson,
                       created_at AS CreatedAt
                   FROM activity_logs
                   {whereClause}
                   ORDER BY created_at DESC
                   LIMIT @Limit OFFSET @Offset;

                   SELECT COUNT(*)
                   FROM activity_logs
                   {whereClause};
                   """;

        await using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<ActivityLogListItemDto>()).ToArray();
        var totalItems = await multi.ReadFirstAsync<long>();

        return new PagedResult<ActivityLogListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems
        };
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    private static string? NormalizeJsonOrNull(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : json;
    }
}