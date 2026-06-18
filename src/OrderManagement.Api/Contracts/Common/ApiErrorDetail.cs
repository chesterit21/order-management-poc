namespace OrderManagement.Api.Contracts.Common;

public sealed record ApiErrorDetail
{
    public ApiErrorDetail()
    {
    }

    public ApiErrorDetail(string? field, string message, object? metadata = null)
    {
        Field = field;
        Message = message;
        Metadata = metadata;
    }

    public string? Field { get; init; }

    public string Message { get; init; } = string.Empty;

    public object? Metadata { get; init; }
}