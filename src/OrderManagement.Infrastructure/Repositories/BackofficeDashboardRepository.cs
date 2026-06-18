using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Dashboard;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class BackofficeDashboardRepository : IBackofficeDashboardRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public BackofficeDashboardRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<BackofficeDashboardSummaryDto> GetSummaryAsync(
        BackofficeDashboardSummaryQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (allowedStoreIds is not null && allowedStoreIds.Count == 0)
        {
            return Empty(query.StoreId ?? Guid.Empty, null, now);
        }

        var conditions = new List<string>();
        var orderConditions = new List<string>();
        var parameters = new DynamicParameters();

        if (allowedStoreIds is not null)
        {
            conditions.Add("p.store_id = ANY(@AllowedStoreIds)");
            orderConditions.Add("o.store_id = ANY(@AllowedStoreIds)");
            parameters.Add("AllowedStoreIds", allowedStoreIds.ToArray());
        }
        else if (query.StoreId is not null)
        {
            conditions.Add("p.store_id = @StoreId");
            orderConditions.Add("o.store_id = @StoreId");
            parameters.Add("StoreId", query.StoreId.Value);
        }

        parameters.Add("LowStockThreshold", query.LowStockThreshold);
        parameters.Add("TodayStart", now.Date);
        parameters.Add("Now", now);

        var productWhere = conditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", conditions);

        var orderWhere = orderConditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", orderConditions);

        var storeIdForInfo = query.StoreId;
        if (storeIdForInfo is null && allowedStoreIds is not null && allowedStoreIds.Count == 1)
        {
            storeIdForInfo = allowedStoreIds.First();
        }

        var sql = $"""
                   WITH product_summary AS (
                       SELECT
                           COUNT(*)::int AS TotalProducts,
                           COUNT(*) FILTER (WHERE p.is_active = TRUE)::int AS ActiveProducts,
                           COUNT(*) FILTER (WHERE p.is_active = FALSE)::int AS InactiveProducts,
                           COUNT(*) FILTER (WHERE p.stock_quantity <= @LowStockThreshold)::int AS LowStockProducts
                       FROM products p
                       {productWhere}
                   ),
                   order_summary AS (
                       SELECT
                           COUNT(*) FILTER (WHERE o.status = 'Pending')::int AS PendingOrders,
                           COUNT(*) FILTER (WHERE o.status = 'Confirmed')::int AS ConfirmedOrders,
                           COUNT(*) FILTER (WHERE o.status = 'Shipped')::int AS ShippedOrders,
                           COUNT(*) FILTER (WHERE o.status = 'Cancelled')::int AS CancelledOrders,
                           COUNT(*) FILTER (WHERE o.created_at >= @TodayStart)::int AS TodayOrders,
                           COALESCE(SUM(o.total_amount) FILTER (
                               WHERE o.created_at >= @TodayStart
                                 AND o.status <> 'Cancelled'
                           ), 0)::numeric AS TodayRevenue
                       FROM orders o
                       {orderWhere}
                   ),
                   store_info AS (
                       SELECT
                           s.id AS StoreId,
                           s.store_name AS StoreName
                       FROM stores s
                       WHERE s.id = @StoreIdForInfo
                       LIMIT 1
                   )
                   SELECT
                       (SELECT StoreId FROM store_info) AS StoreId,
                       (SELECT StoreName FROM store_info) AS StoreName,
                       ps.TotalProducts AS TotalProducts,
                       ps.ActiveProducts AS ActiveProducts,
                       ps.InactiveProducts AS InactiveProducts,
                       ps.LowStockProducts AS LowStockProducts,
                       os.PendingOrders AS PendingOrders,
                       os.ConfirmedOrders AS ConfirmedOrders,
                       os.ShippedOrders AS ShippedOrders,
                       os.CancelledOrders AS CancelledOrders,
                       os.TodayOrders AS TodayOrders,
                       os.TodayRevenue AS TodayRevenue,
                       @Now AS GeneratedAt
                   FROM product_summary ps
                   CROSS JOIN order_summary os;
                   """;

        if (storeIdForInfo is not null)
        {
            parameters.Add("StoreIdForInfo", storeIdForInfo.Value);
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var result = await connection.QuerySingleAsync<BackofficeDashboardSummaryDto>(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));

        return result;
    }

    private static BackofficeDashboardSummaryDto Empty(
        Guid storeId,
        string? storeName,
        DateTimeOffset now)
    {
        return new BackofficeDashboardSummaryDto
        {
            StoreId = storeId,
            StoreName = storeName,
            TotalProducts = 0,
            ActiveProducts = 0,
            InactiveProducts = 0,
            LowStockProducts = 0,
            PendingOrders = 0,
            ConfirmedOrders = 0,
            ShippedOrders = 0,
            CancelledOrders = 0,
            TodayOrders = 0,
            TodayRevenue = 0,
            GeneratedAt = now
        };
    }
}