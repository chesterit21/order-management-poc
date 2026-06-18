namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record SetProductStatusCommand
{
    public required Guid ProductId { get; init; }

    public required bool IsActive { get; init; }

    public required long ExpectedRowVersion { get; init; }
}