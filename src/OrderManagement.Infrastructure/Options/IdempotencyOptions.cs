namespace OrderManagement.Infrastructure.Options;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    public string HeaderName { get; init; } = "Idempotency-Key";

    public int KeyMaxLength { get; init; } = 200;

    public int InProgressTtlSeconds { get; init; } = 120;

    public int CompletedRecordRetentionDays { get; init; } = 7;

    public int FailedRecordRetentionDays { get; init; } = 1;
}