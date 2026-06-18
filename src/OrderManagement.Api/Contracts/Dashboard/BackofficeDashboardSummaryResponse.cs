using System;

namespace OrderManagement.Api.Contracts.Dashboard;

public sealed record BackofficeDashboardSummaryResponse
{
    public Guid StoreId { get; init; }

    public string? StoreName { get; init; }

    public int TotalProducts { get; init; }

    public int ActiveProducts { get; init; }

    public int InactiveProducts { get; init; }

    public int LowStockProducts { get; init; }

    public int PendingOrders { get; init; }

    public int ConfirmedOrders { get; init; }

    public int ShippedOrders { get; init; }

    public int CancelledOrders { get; init; }

    public int TodayOrders { get; init; }

    public decimal TodayRevenue { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }
}