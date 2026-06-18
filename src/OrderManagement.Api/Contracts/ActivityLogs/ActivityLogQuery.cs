namespace OrderManagement.Api.Contracts.ActivityLogs;

/// <summary>
/// Query parameters for GET /api/v1/internal/activity-logs.
/// All filters are optional; omitting them returns all entries (paged).
/// </summary>
public sealed class ActivityLogQuery
{
    /// <summary>Filter by activity type (e.g. "OrderCreated", "RequestFailed").</summary>
    public string? ActivityType { get; init; }

    /// <summary>Filter by correlation ID to trace a single request end-to-end.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Filter by the user who performed the action.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>Filter by order ID.</summary>
    public Guid? OrderId { get; init; }

    /// <summary>Filter by product ID.</summary>
    public Guid? ProductId { get; init; }

    /// <summary>Filter by payment ID.</summary>
    public Guid? PaymentId { get; init; }

    /// <summary>Filter by error code (e.g. "INSUFFICIENT_STOCK").</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Only return entries with HTTP status code >= this value (e.g. 400 for errors only).</summary>
    public int? MinStatusCode { get; init; }

    /// <summary>Inclusive lower bound on CreatedAt (ISO 8601).</summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>Inclusive upper bound on CreatedAt (ISO 8601).</summary>
    public DateTimeOffset? ToDate { get; init; }

    /// <summary>1-based page number. Defaults to 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size. Defaults to 50, max 200.</summary>
    public int PageSize { get; init; } = 50;
}
