namespace OrderManagement.Application.DTOs.ActivityLogs;

/// <summary>
/// Query parameters for listing activity logs with filtering and pagination.
/// </summary>
public sealed record ActivityLogListQueryDto
{
    /// <summary>
    /// Filter by activity type (e.g. "OrderCreated", "RequestFailed", "LoginFailed").
    /// Null/empty returns all types.
    /// </summary>
    public string? ActivityType { get; init; }

    /// <summary>
    /// Filter by correlation ID to trace a single request across all log entries it produced.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Filter by actor user ID.
    /// </summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>
    /// Filter by order ID.
    /// </summary>
    public Guid? OrderId { get; init; }

    /// <summary>
    /// Filter by product ID.
    /// </summary>
    public Guid? ProductId { get; init; }

    /// <summary>
    /// Filter by payment ID.
    /// </summary>
    public Guid? PaymentId { get; init; }

    /// <summary>
    /// Filter by error code (e.g. "INSUFFICIENT_STOCK", "INVALID_CREDENTIALS").
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Only return entries with HTTP status code >= this value.
    /// Useful for filtering error logs (e.g. 400 to see only client errors).
    /// </summary>
    public int? MinStatusCode { get; init; }

    /// <summary>
    /// Inclusive lower bound on CreatedAt.
    /// </summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>
    /// Inclusive upper bound on CreatedAt.
    /// </summary>
    public DateTimeOffset? ToDate { get; init; }

    /// <summary>
    /// 1-based page number. Defaults to 1.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size. Defaults to 50, max 200.
    /// </summary>
    public int PageSize { get; init; } = 50;
}
