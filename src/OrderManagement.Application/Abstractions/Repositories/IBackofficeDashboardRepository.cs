using OrderManagement.Application.DTOs.Dashboard;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IBackofficeDashboardRepository
{
    Task<BackofficeDashboardSummaryDto> GetSummaryAsync(
        BackofficeDashboardSummaryQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}