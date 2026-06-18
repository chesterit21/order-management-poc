using Dapper;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.DTOs.ActivityLogs;
using System.Text.Json;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.ValueObjects;
using OrderManagement.Domain.Rules.Facts;
using System.Data;
using System.Data.Common;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IOrderRulesService _orderRulesService;
    private readonly ILogger<OrderRepository> _logger;
    private readonly IActivityLogWriter _activityLogWriter;

    public OrderRepository(
        IDbConnectionFactory dbConnectionFactory,
        IOrderRulesService orderRulesService,
        ILogger<OrderRepository> logger,
        IActivityLogWriter activityLogWriter)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _orderRulesService = orderRulesService;
        _logger = logger;
        _activityLogWriter = activityLogWriter;
    }

    private static async Task SetLocalLockTimeoutAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                "SET LOCAL lock_timeout = '5s';",
                transaction: transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
    }

    private static async Task<string> GenerateOrderNumberAsync(
        DbConnection connection,
        DbTransaction transaction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT nextval('order_number_seq') AS OrderNumberValue;
                           """;

        var result = await connection.QuerySingleAsync<long>(
            new CommandDefinition(
                sql,
                transaction: transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));

        var datePart = now.ToString("yyyyMMdd");
        
        return $"ORD-{datePart}-{result:D6}";
    }

    public async Task<CreateOrderResult> CreateAsync(
        CreateOrderPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // First, lock products for update to prevent stock race conditions
            var productIds = request.Items.Select(i => i.ProductId).Distinct().OrderBy(id => id).ToArray();
            string? orderNumber = null;
            decimal totalAmount = 0m;
            IReadOnlyCollection<CreateOrderItemResult> resultItems = [];
            
            if (productIds.Length > 0)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        SELECT id, stock_quantity
                        FROM products
                        WHERE id = ANY(@ProductIds)
                        ORDER BY id
                        FOR UPDATE;
                        """,
                        new { ProductIds = productIds },
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                await SetLocalLockTimeoutAsync(connection, transaction, cancellationToken);

                // Generate order number using sequence
                orderNumber = await GenerateOrderNumberAsync(
                    connection,
                    transaction,
                    request.Now,
                    cancellationToken);

                // Get product details including store_id
                var productDetails = await connection.QueryAsync<(Guid Id, decimal Price, Guid StoreId, int StockQuantity, string Name)>(
                    new CommandDefinition(
                        """
                        SELECT id, price, store_id, stock_quantity, name
                        FROM products
                        WHERE id = ANY(@ProductIds);
                        """,
                        new { ProductIds = productIds },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                // Validate all products belong to the same store
                var storeIds = productDetails.Select(p => p.StoreId).Distinct().ToArray();
                if (storeIds.Length != 1)
                {
                    throw new BusinessRuleAppException(
                        ErrorCodes.MixedStoreOrderNotAllowed,
                        "All products in an order must belong to the same store.");
                }

                var storeId = storeIds[0];

                // Calculate total amount
                var productPriceMap = productDetails.ToDictionary(p => p.Id, p => p.Price);
                foreach (var item in request.Items)
                {
                    var price = productPriceMap[item.ProductId];
                    totalAmount += price * item.Quantity;
                }

                // Insert order
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO orders
                            (id, order_number, customer_id, status, shipping_address, total_amount, store_id,
                             row_version, created_by, updated_by, created_at, updated_at)
                        VALUES
                            (@Id, @OrderNumber, @CustomerId, @Status, @ShippingAddress, @TotalAmount, @StoreId,
                             @RowVersion, @CreatedBy, NULL, @Now, @Now);
                        """,
                        new
                        {
                            Id = request.OrderId,
                            OrderNumber = orderNumber,
                            CustomerId = request.CustomerId,
                            Status = OrderStatus.Pending.ToString(),
                            ShippingAddress = request.ShippingAddress,
                            TotalAmount = totalAmount,
                            StoreId = storeId,
                            RowVersion = 1L,
                            CreatedBy = request.CreatedBy,
                            Now = request.Now
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                // Insert order items
                var orderItems = request.Items.Select(item => new
                {
                    OrderId = request.OrderId,
                    item.ProductId,
                    item.Quantity,
                    Price = productPriceMap[item.ProductId],
                    UnitPriceSnapshot = productPriceMap[item.ProductId],
                    Subtotal = productPriceMap[item.ProductId] * item.Quantity,
                    LineTotal = productPriceMap[item.ProductId] * item.Quantity,
                    CreatedAt = request.Now
                }).ToArray();

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO order_items
                            (order_id, product_id, quantity, unit_price_snapshot, price, subtotal, line_total, created_at)
                        VALUES
                            (@OrderId, @ProductId, @Quantity, @UnitPriceSnapshot, @Price, @Subtotal, @LineTotal, @CreatedAt);
                        """,
                        orderItems,
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                // Deduct stock
                var stockUpdates = request.Items.Select(item => new
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Now = request.Now
                }).ToArray();

                var stockUpdateResult = await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        UPDATE products
                        SET stock_quantity = stock_quantity - @Quantity,
                            updated_at = @Now
                        WHERE id = @ProductId
                          AND stock_quantity >= @Quantity;  -- Ensure sufficient stock
                        """,
                        stockUpdates,
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                // Check if all stock updates succeeded
                if (stockUpdateResult != request.Items.Count())
                {
                    // Identify the first product with insufficient stock to provide
                    // a detailed, client-friendly 409 Conflict error response.
                    // Since we hold a FOR UPDATE lock on these products, the stock
                    // values in productDetails are still current at this point.
                    var productStockMap = productDetails.ToDictionary(p => p.Id, p => (p.StockQuantity, p.Name));
                    foreach (var item in request.Items)
                    {
                        if (productStockMap.TryGetValue(item.ProductId, out var info) &&
                            info.StockQuantity < item.Quantity)
                        {
                            throw ConflictAppException.InsufficientStock(
                                item.ProductId,
                                info.Name,
                                item.Quantity,
                                info.StockQuantity,
                                field: "items");
                        }
                    }

                    // Fallback: if we couldn't pinpoint a specific product (edge case
                    // during concurrent races), still return 409 Conflict.
                    throw new ConflictAppException(
                        ErrorCodes.InsufficientStock,
                        "One or more products had insufficient stock during order creation.");
                }

                // Insert inventory movements
                var inventoryMovements = request.Items.Select(item => new
                {
                    MovementId = Guid.NewGuid(),
                    item.ProductId,
                    OrderId = request.OrderId,
                    MovementType = InventoryMovementType.OrderCreatedDeduction.ToString(),
                    Quantity = item.Quantity,
                    StockBefore = productDetails.First(p => p.Id == item.ProductId).StockQuantity, // Get original stock before deduction
                    StockAfter = productDetails.First(p => p.Id == item.ProductId).StockQuantity - item.Quantity, // Calculate stock after deduction
                    Reason = $"Stock deduction for order {orderNumber}",
                    CreatedBy = request.CreatedBy,
                    CreatedAt = request.Now
                }).ToArray();

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO inventory_movements
                            (id, product_id, order_id, movement_type, quantity, stock_before, stock_after, reason, created_by, created_at)
                        VALUES
                            (@MovementId, @ProductId, @OrderId, @MovementType, @Quantity, @StockBefore, @StockAfter, @Reason, @CreatedBy, @CreatedAt);
                        """,
                        inventoryMovements,
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);

                _activityLogWriter.TryWrite(
                    ActivityLogTypes.OrderCreated,
                    orderId: request.OrderId,
                    orderNumber: orderNumber,
                    afterState: new
                    {
                        status = OrderStatus.Pending.ToString(),
                        totalAmount
                    },
                    metadata: new
                    {
                        customerId = request.CustomerId,
                        itemCount = orderItems.Length
                    });

                foreach (var item in request.Items)
                {
                    var product = productDetails.First(p => p.Id == item.ProductId);

                    _activityLogWriter.TryWrite(
                        ActivityLogTypes.StockDeduction,
                        orderId: request.OrderId,
                        orderNumber: orderNumber,
                        productId: product.Id,
                        beforeState: new
                        {
                            stockQuantity = product.StockQuantity
                        },
                        afterState: new
                        {
                            stockQuantity = product.StockQuantity - item.Quantity
                        },
                        metadata: new
                        {
                            quantity = item.Quantity,
                            productName = product.Name
                        });
                }

                // Build result items with product details that were already fetched
                var productDetailMap = productDetails.ToDictionary(p => p.Id);

                resultItems = request.Items.Select(item =>
                {
                    var detail = productDetailMap.GetValueOrDefault(item.ProductId);

                    return new CreateOrderItemResult
                    {
                        ProductId = item.ProductId,
                        ProductName = detail.Name ?? "",
                        Quantity = item.Quantity,
                        UnitPrice = detail.Price,
                        LineTotal = detail.Price * item.Quantity
                    };
                }).ToList();
            }

            return new CreateOrderResult
            {
                Id = request.OrderId,
                OrderNumber = orderNumber ?? string.Empty,
                CustomerId = request.CustomerId,
                Status = OrderStatus.Pending.ToString(),
                ShippingAddress = request.ShippingAddress,
                TotalAmount = totalAmount,
                RowVersion = 1L,
                Items = resultItems,
                CreatedAt = request.Now.DateTime
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<UpdateOrderStatusResult> UpdateStatusAsync(
        UpdateOrderStatusPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await SetLocalLockTimeoutAsync(connection, transaction, cancellationToken);

            var order = await LockOrderAsync(connection, transaction, request.OrderId, cancellationToken);

            if (order is null)
            {
                throw NotFoundAppException.Order(request.OrderId);
            }

            var currentStatus = ParseStatus(order.Status);

            if (order.RowVersion != request.ExpectedRowVersion)
            {
                throw ConcurrencyAppException.RowVersionMismatch(
                    request.ExpectedRowVersion,
                    order.RowVersion);
            }

            // NOTE: Role-based authorization (ApplicationAdmin-only) is enforced in the
            // application service layer (OrderService.UpdateStatusAsync). The repository
            // no longer duplicates this check.

            var ruleResult = _orderRulesService.ValidateOrderTransition(
                new OrderTransitionFact
                {
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    CurrentStatus = currentStatus,
                    TargetStatus = request.TargetStatus,
                    RequestedByUserId = request.UpdatedBy,
                    RequestedByRole = request.UpdatedByRole
                });

            if (!ruleResult.IsAllowed)
            {
                _activityLogWriter.TryWrite(
                    ActivityLogTypes.OrderStatusRejected,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    errorCode: ruleResult.ErrorCode ?? ErrorCodes.InvalidOrderStatusTransition,
                    beforeState: new
                    {
                        status = currentStatus.ToString(),
                        rowVersion = order.RowVersion
                    },
                    afterState: new
                    {
                        targetStatus = request.TargetStatus.ToString()
                    },
                    metadata: new
                    {
                        reason = ruleResult.ErrorMessage,
                        requestedBy = request.UpdatedBy,
                        requestedByRole = request.UpdatedByRole.ToString()
                    });

                throw new BusinessRuleAppException(
                    ruleResult.ErrorCode ?? ErrorCodes.InvalidOrderStatusTransition,
                    ruleResult.ErrorMessage ?? "Order status transition is invalid.");
            }

            var nextRowVersion = order.RowVersion + 1;

            var affectedRows = await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE orders
                    SET
                        status = @TargetStatus,
                        row_version = @NextRowVersion,
                        updated_by = @UpdatedBy,
                        updated_at = @Now
                    WHERE id = @OrderId
                      AND row_version = @CurrentRowVersion;
                    """,
                    new
                    {
                        OrderId = order.Id,
                        TargetStatus = request.TargetStatus.ToString(),
                        NextRowVersion = nextRowVersion,
                        UpdatedBy = request.UpdatedBy,
                        Now = request.Now,
                        CurrentRowVersion = order.RowVersion
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            if (affectedRows != 1)
            {
                throw new ConcurrencyAppException(
                    "Order has been modified by another user. Please refresh and try again.");
            }

            await InsertStatusHistoryAsync(
                connection,
                transaction,
                order.Id,
                currentStatus,
                request.TargetStatus,
                request.Reason ?? $"Status updated by {request.UpdatedByRole} to {request.TargetStatus}.",
                request.UpdatedBy,
                request.Now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _activityLogWriter.TryWrite(
                ActivityLogTypes.OrderStatusUpdated,
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                beforeState: new
                {
                    status = currentStatus.ToString(),
                    rowVersion = order.RowVersion
                },
                afterState: new
                {
                    status = request.TargetStatus.ToString(),
                    rowVersion = nextRowVersion
                },
                metadata: new
                {
                    reason = request.Reason,
                    updatedBy = request.UpdatedBy,
                    updatedByRole = request.UpdatedByRole.ToString()
                });

            return new UpdateOrderStatusResult
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                PreviousStatus = currentStatus.ToString(),
                CurrentStatus = request.TargetStatus.ToString(),
                RowVersion = nextRowVersion,
                UpdatedAt = request.Now.DateTime
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CancelOrderResult> CancelAsync(
        CancelOrderPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await SetLocalLockTimeoutAsync(connection, transaction, cancellationToken);

            var order = await LockOrderAsync(connection, transaction, request.OrderId, cancellationToken);

            if (order is null)
            {
                throw NotFoundAppException.Order(request.OrderId);
            }

            var currentStatus = ParseStatus(order.Status);

            if (order.RowVersion != request.ExpectedRowVersion)
            {
                throw ConcurrencyAppException.RowVersionMismatch(
                    request.ExpectedRowVersion,
                    order.RowVersion);
            }

            // NOTE: Authorization checks (buyer-owns-order, role whitelist) have been
            // moved to the application service layer (OrderService.CancelAsync).
            // The repository now focuses purely on persistence + concurrency control.
            // The OrderCancellationPolicy in the service layer already rejects DevOps
            // and enforces buyer reason constraints.

            var ruleResult = _orderRulesService.ValidateCancel(
                new CancelOrderFact
                {
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    CurrentStatus = currentStatus,
                    RequestedByUserId = request.CancelledBy,
                    RequestedByRole = request.CancelledByRole
                });

            if (!ruleResult.IsAllowed)
            {
                _activityLogWriter.TryWrite(
                    ActivityLogTypes.OrderStatusRejected,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    errorCode: ruleResult.ErrorCode ?? ErrorCodes.InvalidOrderStatusTransition,
                    beforeState: new
                    {
                        status = currentStatus.ToString(),
                        rowVersion = order.RowVersion
                    },
                    afterState: new
                    {
                        targetStatus = OrderStatus.Cancelled.ToString()
                    },
                    metadata: new
                    {
                        reason = ruleResult.ErrorMessage,
                        cancelledBy = request.CancelledBy,
                        cancelledByRole = request.CancelledByRole.ToString(),
                        cancellationReason = request.CancellationReason.ToString()
                    });

                throw new BusinessRuleAppException(
                    ruleResult.ErrorCode ?? ErrorCodes.InvalidOrderStatusTransition,
                    ruleResult.ErrorMessage ?? "Order status transition is invalid.");
            }

            var orderItems = (await connection.QueryAsync<OrderItemQuantityRow>(
                new CommandDefinition(
                    """
                    SELECT
                        product_id AS ProductId,
                        SUM(quantity)::int AS Quantity
                    FROM order_items
                    WHERE order_id = @OrderId
                    GROUP BY product_id
                    ORDER BY product_id;
                    """,
                    new { OrderId = order.Id },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken))).AsList();

            var productIds = orderItems.Select(item => item.ProductId).ToArray();

            var lockedProducts = (await connection.QueryAsync<LockedProductRow>(
                new CommandDefinition(
                    """
                    SELECT
                        id AS Id,
                        sku AS Sku,
                        name AS Name,
                        stock_quantity AS StockQuantity,
                        price AS Price,
                        row_version AS RowVersion,
                        is_active AS IsActive
                    FROM products
                    WHERE id = ANY(@ProductIds)
                    ORDER BY id
                    FOR UPDATE;
                    """,
                    new { ProductIds = productIds },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken))).AsList();

            if (lockedProducts.Count != orderItems.Count)
            {
                throw new ConflictAppException(
                    ErrorCodes.ConcurrentUpdateConflict,
                    "One or more products in this order no longer exist.");
            }

            var productById = lockedProducts.ToDictionary(product => product.Id);

            var stockRestored = new List<StockRestoredResult>(orderItems.Count);
            var stockNotRestored = new List<StockNotRestoredResult>(orderItems.Count);

            foreach (var item in orderItems)
            {
                var product = productById[item.ProductId];
                var stockBefore = product.StockQuantity;
                var stockAfter = request.RestoreStock
                    ? stockBefore + item.Quantity
                    : stockBefore;

                if (request.RestoreStock)
                {
                    var affectedRows = await connection.ExecuteAsync(
                        new CommandDefinition(
                            """
                            UPDATE products
                            SET
                                stock_quantity = @StockAfter,
                                row_version = row_version + 1,
                                updated_at = @Now
                            WHERE id = @ProductId
                              AND stock_quantity = @StockBefore;
                            """,
                            new
                            {
                                ProductId = product.Id,
                                StockBefore = stockBefore,
                                StockAfter = stockAfter,
                                request.Now
                            },
                            transaction,
                            commandTimeout: 30,
                            cancellationToken: cancellationToken));

                    if (affectedRows != 1)
                    {
                        throw new ConflictAppException(
                            ErrorCodes.ConcurrentUpdateConflict,
                            "Product stock was modified concurrently. Please retry.");
                    }

                    stockRestored.Add(new StockRestoredResult
                    {
                        ProductId = product.Id,
                        Quantity = item.Quantity
                    });
                }
                else
                {
                    stockNotRestored.Add(new StockNotRestoredResult
                    {
                        ProductId = product.Id,
                        Quantity = item.Quantity,
                        Reason = request.CancellationReason.ToString()
                    });
                }

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO inventory_movements
                            (id, product_id, order_id, movement_type, quantity,
                             stock_before, stock_after, reason, created_by, created_at)
                        VALUES
                            (@Id, @ProductId, @OrderId, @MovementType, @Quantity,
                             @StockBefore, @StockAfter, @Reason, @CreatedBy, @Now);
                        """,
                        new
                        {
                            Id = Guid.NewGuid(),
                            ProductId = product.Id,
                            OrderId = order.Id,
                            MovementType = request.RestoreStock
                                ? InventoryMovementType.OrderCancelledRestore.ToString()
                                : InventoryMovementType.OrderCancelledNoRestore.ToString(),
                            Quantity = item.Quantity,
                            StockBefore = stockBefore,
                            StockAfter = stockAfter,
                            Reason = request.Reason,
                            CreatedBy = request.CancelledBy,
                            request.Now
                        },
                        transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
            }

            var refundRequired = await MarkPaidPaymentsRefundRequiredAsync(
                connection,
                transaction,
                order.Id,
                request.Now,
                cancellationToken);

            var nextRowVersion = order.RowVersion + 1;

            var orderAffectedRows = await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE orders
                    SET
                        status = @CancelledStatus,
                        row_version = @NextRowVersion,
                        updated_by = @UpdatedBy,
                        updated_at = @Now
                    WHERE id = @OrderId
                      AND row_version = @CurrentRowVersion;
                    """,
                    new
                    {
                        OrderId = order.Id,
                        CancelledStatus = OrderStatus.Cancelled.ToString(),
                        NextRowVersion = nextRowVersion,
                        UpdatedBy = request.CancelledBy,
                        Now = request.Now,
                        CurrentRowVersion = order.RowVersion
                    },
                    transaction,
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));

            if (orderAffectedRows != 1)
            {
                throw new ConcurrencyAppException(
                    "Order has been modified by another user. Please refresh and try again.");
            }

            await InsertStatusHistoryAsync(
                connection,
                transaction,
                order.Id,
                currentStatus,
                OrderStatus.Cancelled,
                request.Reason ?? $"Order cancelled. Cancellation reason: {request.CancellationReason}.",
                request.CancelledBy,
                request.Now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _activityLogWriter.TryWrite(
                ActivityLogTypes.OrderCancelled,
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                beforeState: new
                {
                    status = currentStatus.ToString(),
                    rowVersion = order.RowVersion
                },
                afterState: new
                {
                    status = OrderStatus.Cancelled.ToString(),
                    rowVersion = nextRowVersion
                },
                metadata: new
                {
                    cancellationReason = request.CancellationReason.ToString(),
                    restoreStock = request.RestoreStock,
                    cancelledBy = request.CancelledBy,
                    cancelledByRole = request.CancelledByRole.ToString(),
                    paymentRefundRequired = refundRequired
                });

            foreach (var item in stockRestored)
            {
                _activityLogWriter.TryWrite(
                    ActivityLogTypes.StockRestored,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    productId: item.ProductId,
                    metadata: new
                    {
                        item.Quantity,
                        cancellationReason = request.CancellationReason.ToString()
                    });
            }

            foreach (var item in stockNotRestored)
            {
                _activityLogWriter.TryWrite(
                    ActivityLogTypes.StockNotRestored,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    productId: item.ProductId,
                    metadata: new
                    {
                        item.Quantity,
                        item.Reason,
                        cancellationReason = request.CancellationReason.ToString()
                    });
            }

            if (refundRequired)
            {
                _activityLogWriter.TryWrite(
                    ActivityLogTypes.PaymentRefundRequired,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    metadata: new
                    {
                        reason = "Order cancelled after payment was paid.",
                        cancellationReason = request.CancellationReason.ToString()
                    });
            }

            return new CancelOrderResult
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                PreviousStatus = currentStatus.ToString(),
                CurrentStatus = OrderStatus.Cancelled.ToString(),
                CancellationReason = request.CancellationReason.ToString(),
                StockRestoreApplied = request.RestoreStock,
                RowVersion = nextRowVersion,
                StockRestored = stockRestored,
                StockNotRestored = stockNotRestored,
                PaymentRefundRequired = refundRequired,
                UpdatedAt = request.Now.DateTime
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<GetOrderResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string orderSql = """
                                SELECT
                                    o.id AS Id,
                                    o.order_number AS OrderNumber,
                                    o.customer_id AS CustomerId,
                                    u.display_name AS CustomerName,
                                    o.status AS Status,
                                    o.shipping_address AS ShippingAddress,
                                    o.total_amount AS TotalAmount,
                                    o.row_version AS RowVersion,
                                    o.created_at AS CreatedAt,
                                    o.updated_at AS UpdatedAt
                                FROM orders o
                                INNER JOIN users u ON u.id = o.customer_id
                                WHERE o.id = @OrderId
                                LIMIT 1;
                                """;

        var order = await connection.QuerySingleOrDefaultAsync<OrderDetailRow>(
            new CommandDefinition(
                orderSql,
                new { OrderId = id },
                commandTimeout: 30,
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

        var items = await connection.QueryAsync<OrderItemResult>(
            new CommandDefinition(
                itemsSql,
                new { OrderId = id },
                commandTimeout: 30,
                cancellationToken: cancellationToken));

        var history = await connection.QueryAsync<OrderStatusHistoryResult>(
            new CommandDefinition(
                historySql,
                new { OrderId = id },
                commandTimeout: 30,
                cancellationToken: cancellationToken));

        return new GetOrderResult
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
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

    /// <summary>
    /// Lightweight read-only projection for authorization checks.
    /// Does NOT take a row lock — safe for pre-mutation auth verification.
    /// </summary>
    public async Task<OrderOwnershipResult?> GetOrderOwnershipAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<OrderOwnershipResult>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    customer_id AS CustomerId,
                    status AS Status,
                    row_version AS RowVersion,
                    order_number AS OrderNumber
                FROM orders
                WHERE id = @Id;
                """,
                new { Id = id },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
    }

    public async Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        CancellationToken cancellationToken = default)
    {
        var offset = (query.Page - 1) * query.PageSize;

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

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

        // Apply store filter if provided
        if (allowedStoreIds is not null && allowedStoreIds.Any())
        {
            conditions.Add("o.store_id = ANY(@AllowedStoreIds)");
            parameters.Add("AllowedStoreIds", allowedStoreIds.ToArray());
        }

        parameters.Add("PageSize", query.PageSize);
        parameters.Add("Offset", offset);

        var whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";

        var countSql = $"""
                        SELECT COUNT(*)
                        FROM orders o
                        INNER JOIN users u ON u.id = o.customer_id
                        {whereClause};
                        """;

        var dataSql = $"""
                       SELECT
                           o.id AS Id,
                           o.order_number AS OrderNumber,
                           o.customer_id AS CustomerId,
                           u.display_name AS CustomerName,
                           o.status AS Status,
                           o.total_amount AS TotalAmount,
                           o.row_version AS RowVersion,
                           o.created_at AS CreatedAt,
                           o.updated_at AS UpdatedAt
                       FROM orders o
                       INNER JOIN users u ON u.id = o.customer_id
                       {whereClause}
                       ORDER BY o.created_at DESC, o.id DESC
                       LIMIT @PageSize OFFSET @Offset;
                       """;

        await using var connection = await _dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                countSql,
                parameters,
                commandTimeout: 30,
                cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<OrderListItemResult>(
            new CommandDefinition(
                dataSql,
                parameters,
                commandTimeout: 30,
                cancellationToken: cancellationToken));

        return new PagedResult<OrderListItemResult>
        {
            Items = items.AsList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    private static async Task<LockedOrderRow?> LockOrderAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               order_number AS OrderNumber,
                               customer_id AS CustomerId,
                               status AS Status,
                               total_amount AS TotalAmount,
                               row_version AS RowVersion
                           FROM orders
                           WHERE id = @OrderId
                           FOR UPDATE;
                           """;

        return await connection.QuerySingleOrDefaultAsync<LockedOrderRow>(
            new CommandDefinition(
                sql,
                new { OrderId = orderId },
                transaction: transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
    }

    private static async Task<bool> MarkPaidPaymentsRefundRequiredAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid orderId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string updateSql = """
                                 UPDATE payments
                                 SET
                                     status = @RefundRequiredStatus,
                                     updated_at = @Now
                                 WHERE order_id = @OrderId
                                   AND status = @PaidStatus;
                                 """;

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                updateSql,
                new
                {
                    OrderId = orderId,
                    RefundRequiredStatus = PaymentStatus.RefundRequired.ToString(),
                    PaidStatus = PaymentStatus.Paid.ToString(),
                    Now = now
                },
                transaction: transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));

        return affectedRows > 0;
    }

    private static async Task InsertStatusHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid orderId,
        OrderStatus fromStatus,
        OrderStatus toStatus,
        string reason,
        Guid changedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO order_status_history
                    (id, order_id, from_status, to_status, reason, changed_by, created_at)
                VALUES
                    (@Id, @OrderId, @FromStatus, @ToStatus, @Reason, @ChangedBy, @Now);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    FromStatus = fromStatus.ToString(),
                    ToStatus = toStatus.ToString(),
                    Reason = reason,
                    ChangedBy = changedBy,
                    Now = now
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
    }

    private static OrderStatus ParseStatus(string status)
    {
        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"Invalid order status value '{status}' in database.");
        }

        return parsed;
    }

    private static string NormalizeStatus(string status)
    {
        return Enum.Parse<OrderStatus>(status, ignoreCase: true).ToString();
    }

    private sealed class LockedOrderRow
    {
        public Guid Id { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public Guid CustomerId { get; init; }

        public string Status { get; init; } = string.Empty;

        public decimal TotalAmount { get; init; }

        public long RowVersion { get; init; }
    }

    private sealed class LockedProductRow
    {
        public Guid Id { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public int StockQuantity { get; init; }

        public decimal Price { get; init; }

        public long RowVersion { get; init; }

        public bool IsActive { get; init; }
    }

    private sealed class OrderItemInsertRow
    {
        public Guid Id { get; init; }

        public Guid OrderId { get; init; }

        public Guid ProductId { get; init; }

        public string ProductNameSnapshot { get; init; } = string.Empty;

        public decimal UnitPriceSnapshot { get; init; }

        public int Quantity { get; init; }

        public decimal LineTotal { get; init; }
    }

    private sealed class OrderDetailRow
    {
        public Guid Id { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public Guid CustomerId { get; init; }

        public string CustomerName { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string ShippingAddress { get; init; } = string.Empty;

        public decimal TotalAmount { get; init; }

        public long RowVersion { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class OrderItemQuantityRow
    {
        public Guid ProductId { get; init; }

        public int Quantity { get; init; }
    }
}
