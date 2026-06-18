namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record AdjustProductStockCommand
{
    public required Guid ProductId { get; init; }

    public required string AdjustmentType { get; init; }

    public required int Quantity { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }
}