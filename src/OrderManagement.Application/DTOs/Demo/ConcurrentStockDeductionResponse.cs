namespace OrderManagement.Application.DTOs.Demo;

public sealed class ConcurrentStockDeductionResponse
{
    public required string Scenario { get; init; }
    public int InitialStock { get; init; }
    public int QuantityEach { get; init; }
    public required IReadOnlyCollection<DemoRequestResult> Requests { get; init; }
    public int FinalStock { get; init; }
    public required string Summary { get; init; }
}

public sealed class DemoRequestResult
{
    public required string IdempotencyKey { get; init; }
    public int StatusCode { get; init; }
    public string? OrderId { get; init; }
    public string? OrderNumber { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
