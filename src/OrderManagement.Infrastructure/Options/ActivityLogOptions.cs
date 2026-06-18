namespace OrderManagement.Infrastructure.Options;

public sealed class ActivityLogOptions
{
    public const string SectionName = "ActivityLog";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Capacity of the normal-priority channel (success/info logs).
    /// When full, normal-priority entries are dropped (best-effort).
    /// </summary>
    public int QueueCapacity { get; init; } = 10_000;

    /// <summary>
    /// Capacity of the high-priority channel (error/failure logs).
    /// High-priority entries (RequestFailed, errorCode != null, statusCode >= 400)
    /// are routed here and are NOT subject to the normal drop policy — they get
    /// their own reserved capacity so ops-critical logs survive traffic spikes.
    /// </summary>
    public int HighPriorityQueueCapacity { get; init; } = 2_000;

    public int MaxBatchSize { get; init; } = 100;

    public int FlushIntervalMilliseconds { get; init; } = 1_000;

    public bool DropWhenQueueFull { get; init; } = true;
}