namespace OrderManagement.Application.DTOs.ActivityLogs;

/// <summary>
/// Query parameters for listing activity logs (v2) with LIKE filtering and pagination.
/// </summary>
public sealed record ActivityLogListV2QueryDto
{
    /// <summary>
    /// Filter by correlation ID (LIKE matching).
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Filter by order number (LIKE matching).
    /// </summary>
    public string? OrderNumber { get; init; }

    /// <summary>
    /// Filter by activity type (LIKE matching).
    /// </summary>
    public string? ActivityType { get; init; }

    /// <summary>
    /// 1-based page number. Defaults to 1.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size. Defaults to 50, max 200.
    /// </summary>
    public int PageSize { get; init; } = 50;
}
