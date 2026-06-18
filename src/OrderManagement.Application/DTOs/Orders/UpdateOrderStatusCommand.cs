namespace OrderManagement.Application.DTOs.Orders;

public sealed record UpdateOrderStatusCommand
{
    public required Guid OrderId { get; init; }

    public required string TargetStatus { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }

    public string? CurrentUserId { get; init; }

    public string? CurrentUserRole { get; init; }
}
