using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class BackofficeOrderRepository : IBackofficeOrderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public BackofficeOrderRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResult<BackofficeOrderListItemDto>> ListAsync(
        BackofficeOrderListQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        CancellationToken cancellationToken = default)
    {
        if (allowedStoreIds is not null && allowedStoreIds.Count == 0)
        {
            return new PagedResult<BackofficeOrderListItemDto>
            {
                Items = [],
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = 0
            };
        }

        var offset = (query.Page - 1) * query.PageSize;

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (allowedStoreIds is not null)
        {
            conditions.Add("o.store_id = ANY(@AllowedStoreIds)");
            parameters.Add("AllowedStoreIds", allowedStoreIds.ToArray());
        }

        if (query.StoreId is not null)
        {
            conditions.Add("o.store_id = @StoreId");
            parameters.Add("StoreId", query.StoreId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            conditions.Add("o.status = @Status");
            parameters.Add("Status", NormalizeStatus(query.Status));
        }

        if (query.CustomerId is not null)
        {
            conditions.Add("o.customer_id = @CustomerId");
            parameters.Add("CustomerId", query.CustomerId.Value);
        }

        if (query.FromDate is not null)
        {
            conditions.Add("o.created_at >= @FromDate");
            parameters.Add("FromDate", query.FromDate.Value);
        }

        if (query.ToDate is not null)
        {
            conditions.Add("o.created_at <= @ToDate");
            parameters.Add("ToDate", query.ToDate.Value);
        }

        // Backoffice orders must be store-owned.
        conditions.Add("o.store_id IS NOT NULL");

        parameters.Add("PageSize", query.PageSize);
        parameters.Add("Offset", offset);

        var whereClause = "WHERE " + string.Join(" AND ", conditions);

        var countSql = $"""
                        SELECT COUNT(*)
                        FROM orders o
                        INNER JOIN stores s ON s.id = o.store_id
                        INNER JOIN users u ON u.id = o.customer_id
                        {whereClause};
                        """;

        var dataSql = $"""
                       SELECT
                           o.id AS Id,
                           o.order_number AS OrderNumber,
                           o.store_id AS StoreId,
                           s.store_name AS StoreName,
                           o.customer_id AS CustomerId,
                           u.display_name AS CustomerName,
                           o.status AS Status,
                           o.total_amount AS TotalAmount,
                           o.row_version AS RowVersion,
                           o.created_at AS CreatedAt,
                           o.updated_at AS UpdatedAt
                       FROM orders o
                       INNER JOIN stores s ON s.id = o.store_id
                       INNER JOIN users u ON u.id = o.customer_id
                       {whereClause}
                       ORDER BY o.created_at DESC, o.id DESC
                       LIMIT @PageSize OFFSET @Offset;
                       """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                countSql,
                parameters,
                cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<BackofficeOrderListItemDto>(
            new CommandDefinition(
                dataSql,
                parameters,
                cancellationToken: cancellationToken));

        return new PagedResult<BackofficeOrderListItemDto>
        {
            Items = items.AsList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<BackofficeOrderDetailDto?> GetDetailByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string orderSql = """
                                SELECT
                                    o.id AS Id,
                                    o.order_number AS OrderNumber,
                                    o.store_id AS StoreId,
                                    s.store_name AS StoreName,
                                    o.customer_id AS CustomerId,
                                    u.display_name AS CustomerName,
                                    o.status AS Status,
                                    o.shipping_address AS ShippingAddress,
                                    o.total_amount AS TotalAmount,
                                    o.row_version AS RowVersion,
                                    o.created_at AS CreatedAt,
                                    o.updated_at AS UpdatedAt
                                FROM orders o
                                INNER JOIN stores s ON s.id = o.store_id
                                INNER JOIN users u ON u.id = o.customer_id
                                WHERE o.id = @OrderId
                                  AND o.store_id IS NOT NULL
                                LIMIT 1;
                                """;

        var order = await connection.QuerySingleOrDefaultAsync<OrderDetailRow>(
            new CommandDefinition(
                orderSql,
                new { OrderId = orderId },
                cancellationToken: cancellationToken));

        if (order is null)
        {
            return null;
        }

        const string itemsSql = """
                                SELECT
                                    product_id AS ProductId,
                                    product_name_snapshot AS ProductName,
                                    quantity AS Quantity,
                                    unit_price_snapshot AS UnitPrice,
                                    line_total AS LineTotal
                                FROM order_items
                                WHERE order_id = @OrderId
                                ORDER BY created_at ASC, id ASC;
                                """;

        const string historySql = """
                                  SELECT
                                      from_status AS FromStatus,
                                      to_status AS ToStatus,
                                      reason AS Reason,
                                      changed_by AS ChangedBy,
                                      created_at AS ChangedAt
                                  FROM order_status_history
                                  WHERE order_id = @OrderId
                                  ORDER BY created_at ASC, id ASC;
                                  """;

        var items = await connection.QueryAsync<BackofficeOrderItemDto>(
            new CommandDefinition(
                itemsSql,
                new { OrderId = orderId },
                cancellationToken: cancellationToken));

        var history = await connection.QueryAsync<BackofficeOrderStatusHistoryDto>(
            new CommandDefinition(
                historySql,
                new { OrderId = orderId },
                cancellationToken: cancellationToken));

        return new BackofficeOrderDetailDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            StoreId = order.StoreId,
            StoreName = order.StoreName,
            CustomerId = order.CustomerId,
            CustomerName = order.CustomerName,
            Status = order.Status,
            ShippingAddress = order.ShippingAddress,
            TotalAmount = order.TotalAmount,
            RowVersion = order.RowVersion,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Items = items.AsList(),
            StatusHistory = history.AsList()
        };
    }

    public async Task<BackofficeOrderAccessDto?> GetAccessAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS OrderId,
                               store_id AS StoreId,
                               customer_id AS CustomerId,
                               status AS Status,
                               row_version AS RowVersion
                           FROM orders
                           WHERE id = @OrderId
                             AND store_id IS NOT NULL
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<BackofficeOrderAccessDto>(
            new CommandDefinition(
                sql,
                new { OrderId = orderId },
                cancellationToken: cancellationToken));
    }

    private static string NormalizeStatus(string status)
    {
        return Enum.Parse<OrderStatus>(status, ignoreCase: true).ToString();
    }

    private sealed class OrderDetailRow
    {
        public Guid Id { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public Guid StoreId { get; init; }

        public string StoreName { get; init; } = string.Empty;

        public Guid CustomerId { get; init; }

        public string CustomerName { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string ShippingAddress { get; init; } = string.Empty;

        public decimal TotalAmount { get; init; }

        public long RowVersion { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }
}