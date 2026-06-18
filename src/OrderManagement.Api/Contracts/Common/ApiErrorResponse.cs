namespace OrderManagement.Api.Contracts.Common;

public sealed record ApiErrorResponse
{
    public ApiError Error { get; init; } = new();
}