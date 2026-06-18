namespace OrderManagement.Api.Contracts.ActivityLogs;

/// <summary>
/// Query parameters for GET /api/v2/internal/activity-logs.
/// All filters are optional; omitting them returns all entries (paged).
/// CorrelationId, OrderNumber, and ActivityType use LIKE filtering.
/// </summary>
public sealed class ActivityLogV2Query
{
    /// <summary>Filter by correlation ID (LIKE matching).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Filter by order number (LIKE matching).</summary>
    public string? OrderNumber { get; init; }

    /// <summary>Filter by activity type (LIKE matching).</summary>
    public string? ActivityType { get; init; }

    /// <summary>1-based page number. Defaults to 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size. Defaults to 50, max 200.</summary>
    public int PageSize { get; init; } = 50;
}
