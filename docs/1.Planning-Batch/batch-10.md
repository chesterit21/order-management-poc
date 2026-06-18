Bener banget bro. **Batch 10 sebelumnya memang sudah cover sebagian**, yaitu:

```text
✅ Customer bisa cancel own order
✅ Admin/Ops bisa cancel order
✅ Cancel hanya Pending/Confirmed
✅ Order row lock FOR UPDATE
✅ Product row lock FOR UPDATE ORDER BY id
✅ Restore stock saat cancel
✅ Double cancel aman karena rowVersion/status berubah
```

Tapi case yang lu sebut **belum cukup proper**:

> Admin cancel karena ternyata stock fisik habis akibat penjualan manual/offline di toko/gudang.

Kalau kasus ini tetap restore stock seperti cancel biasa, **bahaya**.

Contoh:

```text
System stock awal = 10
Online order masuk qty 10 -> system stock jadi 0
Ternyata barang fisik sudah dijual manual/offline, gudang kosong
Admin cancel online order karena StockUnavailable
Kalau system restore +10, system stock jadi 10 padahal fisik 0
```

Itu salah. Jadi harus kita bedakan:

```text
CustomerRequested cancel      => stock restored
Admin Operational cancel      => stock restored
Admin StockUnavailable cancel => stock NOT restored
Admin InventoryMismatch cancel => stock NOT restored
```

Selain itu ada bug desain penting:

```text
PATCH /orders/{id}/status targetStatus=Cancelled
```

Kalau ini dibiarkan, admin bisa mengubah status jadi Cancelled lewat update status biasa dan **bypass stock restore / no-restore logic**.

Jadi Batch 10 memang perlu kita **regenerate V2**.

***

# Batch 10 V2 — Update Status + Cancel Order Proper

## Prinsip Final

```text
1. Cancelled tidak boleh lewat PATCH status.
2. Semua cancel wajib lewat POST /orders/{id}/cancel.
3. Cancel punya cancellationReason.
4. Customer hanya boleh reason CustomerRequested.
5. Admin/Ops boleh reason:
   - CustomerRequested
   - StockUnavailable
   - InventoryMismatch
   - OperationalIssue
   - FraudSuspected
6. Jika reason StockUnavailable atau InventoryMismatch:
   - stock tidak direstore
   - inventory movement dicatat sebagai OrderCancelledNoRestore
7. Jika reason lain:
   - stock direstore
   - inventory movement OrderCancelledRestore
8. Cancel tetap hanya untuk Pending/Confirmed.
```

***

# 1. Database Migration Tambahan

Create file baru:

```text
db/migrations/011_update_inventory_movement_types.sql
```

Isi:

```sql
ALTER TABLE inventory_movements
DROP CONSTRAINT IF EXISTS chk_inventory_movements_type;

ALTER TABLE inventory_movements
ADD CONSTRAINT chk_inventory_movements_type
CHECK (
    movement_type IN (
        'OrderCreatedDeduction',
        'OrderCancelledRestore',
        'OrderCancelledNoRestore',
        'ManualAdjustment'
    )
);
```

***

# 2. Domain Enum Update

## `src/OrderManagement.Domain/Enums/InventoryMovementType.cs`

Replace:

```csharp
namespace OrderManagement.Domain.Enums;

public enum InventoryMovementType
{
    OrderCreatedDeduction = 1,
    OrderCancelledRestore = 2,
    OrderCancelledNoRestore = 3,
    ManualAdjustment = 4
}
```

***

## Create `src/OrderManagement.Domain/Enums/OrderCancellationReason.cs`

```csharp
namespace OrderManagement.Domain.Enums;

public enum OrderCancellationReason
{
    CustomerRequested = 1,
    StockUnavailable = 2,
    InventoryMismatch = 3,
    OperationalIssue = 4,
    FraudSuspected = 5
}
```

***

# 3. ErrorCodes Update

## `src/OrderManagement.Application/Constants/ErrorCodes.cs`

Tambahkan constants ini:

```csharp
public const string InvalidCancellationReason = "INVALID_CANCELLATION_REASON";
public const string CancelledStatusRequiresCancelEndpoint = "CANCELLED_STATUS_REQUIRES_CANCEL_ENDPOINT";
```

***

# 4. Cancel DTO Update

## `src/OrderManagement.Application/DTOs/Orders/CancelOrderCommand.cs`

Replace:

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class CancelOrderCommand
{
    public required Guid OrderId { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? CancellationReason { get; init; }

    public string? Reason { get; init; }
}
```

***

## `src/OrderManagement.Application/DTOs/Orders/CancelOrderResult.cs`

Replace:

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class CancelOrderResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required string PreviousStatus { get; init; }

    public required string CurrentStatus { get; init; }

    public required string CancellationReason { get; init; }

    public required bool StockRestoreApplied { get; init; }

    public required long RowVersion { get; init; }

    public required IReadOnlyCollection<StockRestoredResult> StockRestored { get; init; }

    public required IReadOnlyCollection<StockNotRestoredResult> StockNotRestored { get; init; }

    public required bool PaymentRefundRequired { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed class StockRestoredResult
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }
}

public sealed class StockNotRestoredResult
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }

    public required string Reason { get; init; }
}
```

***

## `src/OrderManagement.Application/DTOs/Orders/OrderMutationPersistenceRequests.cs`

Replace:

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.DTOs.Orders;

public sealed class UpdateOrderStatusPersistenceRequest
{
    public required Guid OrderId { get; init; }

    public required OrderStatus TargetStatus { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public required Guid UpdatedBy { get; init; }

    public required UserRole UpdatedByRole { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed class CancelOrderPersistenceRequest
{
    public required Guid OrderId { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public required Guid CancelledBy { get; init; }

    public required UserRole CancelledByRole { get; init; }

    public required OrderCancellationReason CancellationReason { get; init; }

    public required bool RestoreStock { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset Now { get; init; }
}
```

***

# 5. Validators Update

## `UpdateOrderStatusCommandValidator.cs`

Important: `Cancelled` ditolak dari PATCH status.

Replace:

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders;

public sealed class UpdateOrderStatusCommandValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.TargetStatus)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Target status is required.")
            .Must(BeValidOrderStatus)
            .WithMessage("Target status is invalid.")
            .Must(NotCancelledStatus)
            .WithMessage("Use cancel endpoint to cancel an order.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidOrderStatus(string status)
    {
        return Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _);
    }

    private static bool NotCancelledStatus(string status)
    {
        return !Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) ||
               parsed != OrderStatus.Cancelled;
    }
}
```

***

## `CancelOrderCommandValidator.cs`

Replace:

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.CancellationReason)
            .Must(BeValidCancellationReason)
            .WithMessage("Cancellation reason is invalid.")
            .When(command => !string.IsNullOrWhiteSpace(command.CancellationReason));

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidCancellationReason(string? reason)
    {
        return Enum.TryParse<OrderCancellationReason>(reason, ignoreCase: true, out _);
    }
}
```

***

# 6. OrderService Cancel Logic Update

Di `OrderService.cs`, replace method `UpdateStatusAsync` dan `CancelAsync` saja.

## `UpdateStatusAsync`

```csharp
public async Task<UpdateOrderStatusResult> UpdateStatusAsync(
    UpdateOrderStatusCommand command,
    CancellationToken cancellationToken = default)
{
    var validationResult = await _updateStatusValidator.ValidateAsync(command, cancellationToken);

    if (!validationResult.IsValid)
    {
        throw new ValidationAppException(
            "Update order status request validation failed.",
            validationResult.Errors
                .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                .ToArray());
    }

    var currentUserId = GetRequiredCurrentUserId();
    var currentRole = GetRequiredCurrentUserRole();

    if (currentRole is not (UserRole.Admin or UserRole.Ops))
    {
        throw new ForbiddenAppException("Only Admin or Ops can update order status.");
    }

    var targetStatus = Enum.Parse<OrderStatus>(command.TargetStatus, ignoreCase: true);

    if (targetStatus == OrderStatus.Cancelled)
    {
        throw new BusinessRuleAppException(
            ErrorCodes.CancelledStatusRequiresCancelEndpoint,
            "Use cancel endpoint to cancel an order so stock and audit trail are handled correctly.");
    }

    var result = await _orderRepository.UpdateStatusAsync(
        new UpdateOrderStatusPersistenceRequest
        {
            OrderId = command.OrderId,
            TargetStatus = targetStatus,
            ExpectedRowVersion = command.ExpectedRowVersion,
            UpdatedBy = currentUserId,
            UpdatedByRole = currentRole,
            Reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim(),
            Now = _clock.UtcNow
        },
        cancellationToken);

    _logger.LogInformation(
        "Order status updated. OrderId={OrderId} PreviousStatus={PreviousStatus} CurrentStatus={CurrentStatus} UpdatedBy={UpdatedBy}",
        result.Id,
        result.PreviousStatus,
        result.CurrentStatus,
        currentUserId);

    return result;
}
```

***

## `CancelAsync`

```csharp
public async Task<CancelOrderResult> CancelAsync(
    CancelOrderCommand command,
    CancellationToken cancellationToken = default)
{
    var validationResult = await _cancelValidator.ValidateAsync(command, cancellationToken);

    if (!validationResult.IsValid)
    {
        throw new ValidationAppException(
            "Cancel order request validation failed.",
            validationResult.Errors
                .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                .ToArray());
    }

    var currentUserId = GetRequiredCurrentUserId();
    var currentRole = GetRequiredCurrentUserRole();

    var cancellationReason = ResolveCancellationReason(
        command.CancellationReason,
        currentRole);

    var restoreStock = ShouldRestoreStock(cancellationReason);

    var result = await _orderRepository.CancelAsync(
        new CancelOrderPersistenceRequest
        {
            OrderId = command.OrderId,
            ExpectedRowVersion = command.ExpectedRowVersion,
            CancelledBy = currentUserId,
            CancelledByRole = currentRole,
            CancellationReason = cancellationReason,
            RestoreStock = restoreStock,
            Reason = BuildCancellationReasonText(command.Reason, cancellationReason, restoreStock),
            Now = _clock.UtcNow
        },
        cancellationToken);

    _logger.LogInformation(
        "Order cancelled. OrderId={OrderId} PreviousStatus={PreviousStatus} CancelledBy={CancelledBy} CancellationReason={CancellationReason} RestoreStock={RestoreStock}",
        result.Id,
        result.PreviousStatus,
        currentUserId,
        result.CancellationReason,
        result.StockRestoreApplied);

    return result;
}

private static OrderCancellationReason ResolveCancellationReason(
    string? reason,
    UserRole currentRole)
{
    if (string.IsNullOrWhiteSpace(reason))
    {
        return currentRole == UserRole.Customer
            ? OrderCancellationReason.CustomerRequested
            : OrderCancellationReason.OperationalIssue;
    }

    if (!Enum.TryParse<OrderCancellationReason>(reason, ignoreCase: true, out var parsed))
    {
        throw new BusinessRuleAppException(
            ErrorCodes.InvalidCancellationReason,
            "Cancellation reason is invalid.");
    }

    if (currentRole == UserRole.Customer &&
        parsed != OrderCancellationReason.CustomerRequested)
    {
        throw new ForbiddenAppException("Customer can only cancel with CustomerRequested reason.");
    }

    return parsed;
}

private static bool ShouldRestoreStock(OrderCancellationReason reason)
{
    return reason is not OrderCancellationReason.StockUnavailable
        and not OrderCancellationReason.InventoryMismatch;
}

private static string BuildCancellationReasonText(
    string? freeTextReason,
    OrderCancellationReason reason,
    bool restoreStock)
{
    var stockAction = restoreStock
        ? "Stock restored."
        : "Stock was not restored because cancellation reason indicates physical stock is unavailable or mismatched.";

    if (string.IsNullOrWhiteSpace(freeTextReason))
    {
        return $"Cancellation reason: {reason}. {stockAction}";
    }

    return $"Cancellation reason: {reason}. {stockAction} Note: {freeTextReason.Trim()}";
}
```

***

# 7. Repository Cancel Logic Update

Di `OrderRepository.cs`, replace method `CancelAsync` saja.

## `CancelAsync` V2

```csharp
public async Task<CancelOrderResult> CancelAsync(
    CancelOrderPersistenceRequest request,
    CancellationToken cancellationToken = default)
{
    await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

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

        if (request.CancelledByRole == UserRole.Customer &&
            order.CustomerId != request.CancelledBy)
        {
            throw new ForbiddenAppException("Customer can only cancel their own order.");
        }

        if (request.CancelledByRole is not (UserRole.Customer or UserRole.Admin or UserRole.Ops))
        {
            throw new ForbiddenAppException("User is not allowed to cancel order.");
        }

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
            throw new BusinessRuleAppException(
                ruleResult.ErrorCode ?? ErrorCodes.InvalidOrderStatusTransition,
                ruleResult.ErrorMessage ?? $"Order cannot be cancelled because current status is {currentStatus}.");
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
            UpdatedAt = request.Now
        };
    }
    catch
    {
        await transaction.RollbackAsync(cancellationToken);
        throw;
    }
}
```

***

# 8. API Contracts Update

## `CancelOrderRequest.cs`

Replace:

```csharp
namespace OrderManagement.Api.Contracts.Orders;

public sealed class CancelOrderRequest
{
    public long ExpectedRowVersion { get; init; }

    public string? CancellationReason { get; init; }

    public string? Reason { get; init; }
}
```

***

## `CancelOrderResponse.cs`

Replace:

```csharp
namespace OrderManagement.Api.Contracts.Orders;

public sealed class CancelOrderResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public string PreviousStatus { get; init; } = string.Empty;

    public string CurrentStatus { get; init; } = string.Empty;

    public string CancellationReason { get; init; } = string.Empty;

    public bool StockRestoreApplied { get; init; }

    public long RowVersion { get; init; }

    public IReadOnlyCollection<StockRestoredResponse> StockRestored { get; init; } = [];

    public IReadOnlyCollection<StockNotRestoredResponse> StockNotRestored { get; init; } = [];

    public bool PaymentRefundRequired { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class StockRestoredResponse
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }
}

public sealed class StockNotRestoredResponse
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }

    public string Reason { get; init; } = string.Empty;
}
```

***

# 9. OrdersController Cancel Update

Di `OrdersController.cs`, update method `Cancel`.

```csharp
[HttpPost("{id:guid}/cancel")]
[ProducesResponseType(typeof(CancelOrderResponse), StatusCodes.Status200OK)]
public async Task<ActionResult<CancelOrderResponse>> Cancel(
    Guid id,
    [FromBody] CancelOrderRequest request,
    CancellationToken cancellationToken)
{
    var result = await _orderService.CancelAsync(
        new CancelOrderCommand
        {
            OrderId = id,
            ExpectedRowVersion = request.ExpectedRowVersion,
            CancellationReason = request.CancellationReason,
            Reason = request.Reason
        },
        cancellationToken);

    return Ok(new CancelOrderResponse
    {
        Id = result.Id,
        OrderNumber = result.OrderNumber,
        PreviousStatus = result.PreviousStatus,
        CurrentStatus = result.CurrentStatus,
        CancellationReason = result.CancellationReason,
        StockRestoreApplied = result.StockRestoreApplied,
        RowVersion = result.RowVersion,
        PaymentRefundRequired = result.PaymentRefundRequired,
        UpdatedAt = result.UpdatedAt,
        StockRestored = result.StockRestored
            .Select(item => new StockRestoredResponse
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity
            })
            .ToArray(),
        StockNotRestored = result.StockNotRestored
            .Select(item => new StockNotRestoredResponse
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                Reason = item.Reason
            })
            .ToArray()
    });
}
```

***

# 10. Behavior Final

## Customer cancel

Request:

```json
{
  "expectedRowVersion": 1,
  "cancellationReason": "CustomerRequested",
  "reason": "Changed my mind."
}
```

Result:

```json
{
  "currentStatus": "Cancelled",
  "cancellationReason": "CustomerRequested",
  "stockRestoreApplied": true,
  "stockRestored": [
    {
      "productId": "...",
      "quantity": 10
    }
  ],
  "stockNotRestored": []
}
```

***

## Admin cancel karena stock habis fisik

Request:

```json
{
  "expectedRowVersion": 1,
  "cancellationReason": "StockUnavailable",
  "reason": "Warehouse checked physical stock. Item is unavailable due to offline sale."
}
```

Result:

```json
{
  "currentStatus": "Cancelled",
  "cancellationReason": "StockUnavailable",
  "stockRestoreApplied": false,
  "stockRestored": [],
  "stockNotRestored": [
    {
      "productId": "...",
      "quantity": 10,
      "reason": "StockUnavailable"
    }
  ]
}
```

System stock tidak naik. Ini yang benar untuk kasus stok fisik sudah habis.

***

# 11. Important Design Decision

Dengan V2 ini:

```text
Cancel CustomerRequested:
Order created already deducted stock.
Cancel means reservation released.
Therefore stock restored.

Cancel StockUnavailable:
Order created already deducted stock.
But physical stock is gone due offline/manual sale or mismatch.
Cancel must not restore system stock.
Otherwise system stock becomes overstated.
```

Ini jauh lebih aman buat presentasi.

***

# 12. Build & Migration

Run:

```bash
dotnet build
```

Lalu startup API akan apply:

```text
011_update_inventory_movement_types.sql
```

Run:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

***

# 13. Manual Test

## Test admin cancel no restore

```bash
curl -k -i -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/cancel" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"expectedRowVersion\": $ORDER_VERSION,
    \"cancellationReason\": \"StockUnavailable\",
    \"reason\": \"Physical stock unavailable after warehouse check.\"
  }"
```

Cek inventory movement:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT movement_type, quantity, stock_before, stock_after, reason FROM inventory_movements WHERE order_id = '$ORDER_ID' ORDER BY created_at;"
```

Expected movement:

```text
OrderCreatedDeduction       stock 15 -> 5
OrderCancelledNoRestore     stock 5 -> 5
```

***

# 14. Kesimpulan

Lu benar bro. **Batch 10 lama perlu digenerate ulang secara konsep**, karena belum membedakan:

```text
Cancel normal yang harus restore stock
vs
Cancel karena stock fisik unavailable yang tidak boleh restore stock
```

Dengan Batch 10 V2 ini, kita sudah cover:

```text
✅ Buyer cancel order
✅ Admin cancel order
✅ Admin cancel karena stock fisik habis
✅ Stock restore conditional
✅ No stock overstatement
✅ PATCH status tidak bisa bypass cancel flow
✅ Audit inventory tetap ada
✅ Status history tetap ada
✅ Concurrency tetap aman
```
# 15. Commit Batch 10

```bash
git add .
git commit -m "feat: add order status update and cancellation flow"
```
