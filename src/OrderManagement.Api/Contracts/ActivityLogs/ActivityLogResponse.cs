namespace OrderManagement.Api.Contracts.ActivityLogs;

/// <summary>
/// A single activity log entry in the API response.
/// </summary>
public sealed class ActivityLogResponse
{
    public Guid Id { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public string ActivityType { get; init; } = string.Empty;

    public Guid? ActorUserId { get; init; }

    public string? ActorUsername { get; init; }

    public string? ActorRole { get; init; }

    public Guid? OrderId { get; init; }

    public string? OrderNumber { get; init; }

    public Guid? ProductId { get; init; }

    public Guid? PaymentId { get; init; }

    public string? RequestPath { get; init; }

    public string? HttpMethod { get; init; }

    public int? StatusCode { get; init; }

    public long? ElapsedMs { get; init; }

    public string? ErrorCode { get; init; }

    /// <summary>Raw JSON string of the before-state snapshot, or null.</summary>
    public string? BeforeStateJson { get; init; }

    /// <summary>Raw JSON string of the after-state snapshot, or null.</summary>
    public string? AfterStateJson { get; init; }

    /// <summary>Raw JSON string of additional metadata, or null.</summary>
    public string? MetadataJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
