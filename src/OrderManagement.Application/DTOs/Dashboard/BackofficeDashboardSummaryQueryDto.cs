namespace OrderManagement.Application.DTOs.Dashboard;

public sealed record BackofficeDashboardSummaryQueryDto
{
    public Guid StoreId { get; init; }

    public int LowStockThreshold { get; init; } = 5;
}