using Dapper;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Domain.ValueObjects;
using System.Data.Common;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class PaymentRepository(
    IDbConnectionFactory connectionFactory,
    IOrderRulesService orderRulesService,
    ILogger<PaymentRepository> logger,
    IActivityLogWriter activityLogWriter) : IPaymentRepository
{
    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;
    private readonly IOrderRulesService _orderRulesService = orderRulesService;
    private readonly ILogger<PaymentRepository> _logger = logger;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;

    public async Task<CreatePaymentResult> CreateAsync(
        CreatePaymentPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await SetLocalLockTimeoutAsync(connection, transaction, cancellationToken);

            var order = await LockOrderAsync(
                connection,
                transaction,
                request.OrderId,
                cancellationToken);

            if (order is null)
            {
                throw NotFoundAppException.Order(request.OrderId);
            }


            var currentOrderStatus = ParseOrderStatus(order.Status);

            var hasExistingPaidPayment = await HasExistingPaidPaymentAsync(
                connection,
                transaction,
                order.Id,
                cancellationToken);

            var ruleResult = _orderRulesService.ValidatePayment(
                new PaymentFact
                {
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    CurrentOrderStatus = currentOrderStatus,
                    RequestedByUserId = request.RequestedBy,
                    RequestedByRole = request.RequestedByRole,
                    HasExistingPaidPayment = hasExistingPaidPayment
                });

            if (!ruleResult.IsAllowed)
            {
                _activityLogWriter.TryWrite(
                    ActivityLogTypes.PaymentRejected,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    errorCode: ruleResult.ErrorCode ?? ErrorCodes.PaymentNotAllowed,
                    beforeState: new
                    {
                        orderStatus = currentOrderStatus.ToString(),
                        rowVersion = order.RowVersion
                    },
                    metadata: new
                    {
                        reason = ruleResult.ErrorMessage,
                        requestedBy = request.RequestedBy,
                        requestedByRole = request.RequestedByRole.ToString(),
                        hasExistingPaidPayment
                    });

                throw new ConflictAppException(
                    ErrorCodes.PaymentNotAllowed,
                    ruleResult.ErrorMessage ?? "Payment is not allowed for this order.");
            }

            var paymentId = Guid.NewGuid();
            var paymentReference = GeneratePaymentReference(request.Now, paymentId);

            var paymentStatus = request.SimulateResult == PaymentSimulationResult.Success
                ? PaymentStatus.Paid
                : PaymentStatus.Failed;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO payments
                        (id, order_id, amount, status, provider, payment_reference, created_at, updated_at)
                    VALUES
                        (@Id, @OrderId, @Amount, @Status, @Provider, @PaymentReference, @Now, @Now);
                    """,
                    new
                    {
                        Id = paymentId,
                        OrderId = order.Id,
                        Amount = order.TotalAmount,
                        Status = paymentStatus.ToString(),
                        request.Provider,
                        PaymentReference = paymentReference,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            var finalOrderStatus = currentOrderStatus;
            var nextRowVersion = order.RowVersion;

            if (paymentStatus == PaymentStatus.Paid)
            {
                finalOrderStatus = OrderStatus.Confirmed;
                nextRowVersion = order.RowVersion + 1;

                var affectedRows = await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        UPDATE orders
                        SET
                            status = @ConfirmedStatus,
                            row_version = @NextRowVersion,
                            updated_by = @UpdatedBy,
                            updated_at = @Now
                        WHERE id = @OrderId
                          AND row_version = @CurrentRowVersion
                          AND status = @ExpectedStatus;
                        """,
                        new
                        {
                            OrderId = order.Id,
                            ConfirmedStatus = OrderStatus.Confirmed.ToString(),
                            NextRowVersion = nextRowVersion,
                            UpdatedBy = request.RequestedBy,
                            Now = request.Now,
                            CurrentRowVersion = order.RowVersion,
                            ExpectedStatus = OrderStatus.Pending.ToString()
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                if (affectedRows != 1)
                {
                    throw new ConcurrencyAppException(
                        "Order has been modified by another process. Please refresh and try again.");
                }

                await InsertStatusHistoryAsync(
                    connection,
                    transaction,
                    order.Id,
                    OrderStatus.Pending,
                    OrderStatus.Confirmed,
                    "Payment succeeded. Order confirmed.",
                    request.RequestedBy,
                    request.Now,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            _activityLogWriter.TryWrite(
                ActivityLogTypes.PaymentCreated,
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                paymentId: paymentId,
                afterState: new
                {
                    paymentStatus = paymentStatus.ToString(),
                    orderStatus = finalOrderStatus.ToString()
                },
                metadata: new
                {
                    amount = order.TotalAmount,
                    provider = request.Provider,
                    paymentReference,
                    requestedBy = request.RequestedBy,
                    requestedByRole = request.RequestedByRole.ToString()
                });

            if (paymentStatus == PaymentStatus.Paid)
            {
                _activityLogWriter.TryWrite(
                    ActivityLogTypes.PaymentPaid,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    paymentId: paymentId,
                    beforeState: new
                    {
                        orderStatus = OrderStatus.Pending.ToString(),
                        rowVersion = order.RowVersion
                    },
                    afterState: new
                    {
                        orderStatus = OrderStatus.Confirmed.ToString(),
                        rowVersion = nextRowVersion
                    },
                    metadata: new
                    {
                        amount = order.TotalAmount,
                        provider = request.Provider,
                        paymentReference
                    });
            }
            else
            {
                _activityLogWriter.TryWrite(
                    ActivityLogTypes.PaymentFailed,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    paymentId: paymentId,
                    metadata: new
                    {
                        amount = order.TotalAmount,
                        provider = request.Provider,
                        paymentReference
                    });
            }

            return new CreatePaymentResult
            {
                PaymentId = paymentId,
                OrderId = order.Id,
                Amount = order.TotalAmount,
                Status = paymentStatus.ToString(),
                OrderStatus = finalOrderStatus.ToString(),
                Provider = request.Provider,
                PaymentReference = paymentReference,
                CreatedAt = request.Now
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PaymentListResult> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               amount AS Amount,
                               status AS Status,
                               provider AS Provider,
                               payment_reference AS PaymentReference,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM payments
                           WHERE order_id = @OrderId
                           ORDER BY created_at DESC, id DESC;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var payments = await connection.QueryAsync<PaymentResult>(
            new CommandDefinition(
                sql,
                new { OrderId = orderId },
                cancellationToken: cancellationToken));

        return new PaymentListResult
        {
            OrderId = orderId,
            Payments = payments.AsList()
        };
    }

    private static async Task SetLocalLockTimeoutAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                "SET LOCAL lock_timeout = '5s';",
                transaction: transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<LockedOrderForPaymentRow?> LockOrderAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
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

        return await connection.QuerySingleOrDefaultAsync<LockedOrderForPaymentRow>(
            new CommandDefinition(
                sql,
                new { OrderId = orderId },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<bool> HasExistingPaidPaymentAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM payments
                               WHERE order_id = @OrderId
                                 AND status = @PaidStatus
                           );
                           """;

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    OrderId = orderId,
                    PaidStatus = PaymentStatus.Paid.ToString()
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task InsertStatusHistoryAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
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
                cancellationToken: cancellationToken));
    }

    private static OrderStatus ParseOrderStatus(string status)
    {
        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"Invalid order status value '{status}' in database.");
        }

        return parsed;
    }

    private static string GeneratePaymentReference(DateTimeOffset now, Guid paymentId)
    {
        return $"MOCK-{now:yyyyMMdd}-{paymentId.ToString("N")[..12].ToUpperInvariant()}";
    }

    private sealed class LockedOrderForPaymentRow
    {
        public Guid Id { get; init; }

        public string OrderNumber { get; init; } = string.Empty;

        public Guid CustomerId { get; init; }

        public string Status { get; init; } = string.Empty;

        public decimal TotalAmount { get; init; }

        public long RowVersion { get; init; }
    }
}