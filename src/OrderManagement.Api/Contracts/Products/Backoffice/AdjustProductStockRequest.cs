using System.ComponentModel.DataAnnotations;

namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed record AdjustProductStockRequest
{
    [Required]
    public string AdjustmentType { get; init; } = string.Empty;

    [Required]
    public int Quantity { get; init; }

    [Required]
    public long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }
}