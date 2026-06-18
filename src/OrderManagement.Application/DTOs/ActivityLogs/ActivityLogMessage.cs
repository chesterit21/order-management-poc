using System;

namespace OrderManagement.Application.DTOs.ActivityLogs;

public sealed record ActivityLogMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string CorrelationId { get; init; }

    public required string ActivityType { get; init; }

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

    /// <summary>
    /// JSON string. Must be valid JSON if provided.
    /// </summary>
    public string? BeforeStateJson { get; init; }

    /// <summary>
    /// JSON string. Must be valid JSON if provided.
    /// </summary>
    public string? AfterStateJson { get; init; }

    /// <summary>
    /// JSON string. Must be valid JSON if provided.
    /// </summary>
    public string? MetadataJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}