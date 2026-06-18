using OrderManagement.Application.DTOs.Dashboard;

namespace OrderManagement.Application.Abstractions.Dashboard;

public interface IBackofficeDashboardService
{
    Task<BackofficeDashboardSummaryDto> GetSummaryAsync(
        BackofficeDashboardSummaryQueryDto query,
        CancellationToken cancellationToken = default);
}