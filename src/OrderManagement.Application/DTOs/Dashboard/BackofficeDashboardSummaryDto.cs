namespace OrderManagement.Application.DTOs.Dashboard;

public sealed record BackofficeDashboardSummaryDto
{
    public Guid StoreId { get; init; }

    public string? StoreName { get; init; }

    public required int TotalProducts { get; init; }

    public required int ActiveProducts { get; init; }

    public required int InactiveProducts { get; init; }

    public required int LowStockProducts { get; init; }

    public required int PendingOrders { get; init; }

    public required int ConfirmedOrders { get; init; }

    public required int ShippedOrders { get; init; }

    public required int CancelledOrders { get; init; }

    public required int TodayOrders { get; init; }

    public required decimal TodayRevenue { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}