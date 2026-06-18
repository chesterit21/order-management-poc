namespace OrderManagement.Application.DTOs.Idempotency;

public sealed record StoredIdempotencyResponse
{
    public required int StatusCode { get; init; }

    public required string ResponseBody { get; init; }
}