namespace OrderManagement.Api.Contracts.Common;

public sealed record ApiError
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyCollection<ApiErrorDetail> Details { get; init; } = [];

    public string CorrelationId { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }
}