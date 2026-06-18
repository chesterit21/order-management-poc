using System;

namespace OrderManagement.Api.Contracts.Dashboard;

public sealed record BackofficeDashboardSummaryQuery
{
    public Guid? StoreId { get; init; }

    public int LowStockThreshold { get; init; } = 10;
}