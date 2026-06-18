lanjut **Batch 16D — Manual Stock Adjustment + Seller Dashboard Summary**.

Batch ini penting karena menjawab case real ecommerce:

```text
- Stock berubah karena penjualan offline/manual.
- Seller perlu adjust stock dari panel toko.
- Semua stock adjustment harus audited.
- Dashboard seller harus scoped per toko.
- Order harus punya store_id supaya seller dashboard/order panel nanti masuk akal.
```

Di Batch 16D ini kita implement:

```text
- PATCH /api/v1/backoffice/products/{id}/stock
- adjustmentType: Increase / Decrease / Set
- product row lock FOR UPDATE
- validate stock never negative
- inventory_movements ManualAdjustment
- ProductStockAdjusted activity log
- orders.store_id
- create order validates all products same store
- GET /api/v1/backoffice/dashboard/summary
- seller store scoped dashboard
```

***

# Batch 16D — Manual Stock Adjustment + Seller Dashboard Summary

***

## 1. Migration: Orders Store Ownership

Karena dashboard seller harus scoped by store, order juga perlu `store_id`.

Create file:

```text
db/migrations/016_add_store_id_to_orders.sql
```

Isi:

```sql
ALTER TABLE orders
ADD COLUMN IF NOT EXISTS store_id UUID NULL REFERENCES stores(id);

CREATE INDEX IF NOT EXISTS idx_orders_store_id
ON orders(store_id);

CREATE INDEX IF NOT EXISTS idx_orders_store_status
ON orders(store_id, status);

CREATE INDEX IF NOT EXISTS idx_orders_store_created_at
ON orders(store_id, created_at DESC);
```

> `store_id` dibuat nullable supaya existing data lama tidak gagal migration. Untuk order baru, repository akan wajib set `store_id`.

***

## 2. ErrorCodes Update

File:

```text
src/OrderManagement.Application/Constants/ErrorCodes.cs
```

Tambahkan:

```csharp
public const string ProductStoreNotAssigned = "PRODUCT_STORE_NOT_ASSIGNED";
public const string MixedStoreOrderNotAllowed = "MIXED_STORE_ORDER_NOT_ALLOWED";
public const string InvalidStockAdjustment = "INVALID_STOCK_ADJUSTMENT";
```

***

## 3. Domain Enum: Stock Adjustment Type

Create file:

```text
src/OrderManagement.Domain/Enums/StockAdjustmentType.cs
```

```csharp
namespace OrderManagement.Domain.Enums;

public enum StockAdjustmentType
{
    Increase = 1,
    Decrease = 2,
    Set = 3
}
```

Pastikan `InventoryMovementType` sudah punya:

```csharp
ManualAdjustment
```

Kalau belum, final-nya:

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

# 4. DTOs — Stock Adjustment

Buat file:

```text
src/OrderManagement.Application/DTOs/Products/Backoffice/AdjustProductStockCommand.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class AdjustProductStockCommand
{
    public required Guid ProductId { get; init; }

    public required string AdjustmentType { get; init; }

    public required int Quantity { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }
}
```

***

Create file:

```text
src/OrderManagement.Application/DTOs/Products/Backoffice/AdjustProductStockResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class AdjustProductStockResult
{
    public required Guid ProductId { get; init; }

    public required Guid StoreId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string AdjustmentType { get; init; }

    public required int Quantity { get; init; }

    public required int StockBefore { get; init; }

    public required int StockAfter { get; init; }

    public required long RowVersion { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
```

***

Create file:

```text
src/OrderManagement.Application/DTOs/Products/Backoffice/AdjustProductStockPersistenceRequest.cs
```

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class AdjustProductStockPersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required StockAdjustmentType AdjustmentType { get; init; }

    public required int Quantity { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }

    public required Guid AdjustedBy { get; init; }

    public required DateTimeOffset Now { get; init; }
}
```

***

# 5. DTOs — Dashboard Summary

Create folder kalau belum ada:

```text
src/OrderManagement.Application/DTOs/Dashboard
```

Create file:

```text
src/OrderManagement.Application/DTOs/Dashboard/BackofficeDashboardSummaryQueryDto.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Dashboard;

public sealed class BackofficeDashboardSummaryQueryDto
{
    public Guid? StoreId { get; init; }

    public int LowStockThreshold { get; init; } = 5;
}
```

***

Create file:

```text
src/OrderManagement.Application/DTOs/Dashboard/BackofficeDashboardSummaryDto.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Dashboard;

public sealed class BackofficeDashboardSummaryDto
{
    public Guid? StoreId { get; init; }

    public string? StoreName { get; init; }

    public required int TotalProducts { get; init; }

    public required int ActiveProducts { get; init; }

    public required int InactiveProducts { get; init; }

    public required int LowStockProducts { get; init; }

    public required int PendingOrders { get; init; }

    public required int ConfirmedOrders { get; init; }

    public required int ShippedOrders { get; init; }

    public required int CancelledOrders { get; init; }

    public required int TodayOrders { get; init; }

    public required decimal TodayRevenue { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}
```

***

# 6. Validator — Adjust Product Stock

Create file:

```text
src/OrderManagement.Application/Validators/Products/Backoffice/AdjustProductStockCommandValidator.cs
```

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class AdjustProductStockCommandValidator : AbstractValidator<AdjustProductStockCommand>
{
    public AdjustProductStockCommandValidator()
    {
        RuleFor(command => command.ProductId)
            .NotEmpty()
            .WithMessage("Product id is required.");

        RuleFor(command => command.AdjustmentType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Adjustment type is required.")
            .Must(BeValidAdjustmentType)
            .WithMessage("Adjustment type must be Increase, Decrease, or Set.");

        RuleFor(command => command.Quantity)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Quantity cannot be negative.");

        RuleFor(command => command)
            .Must(command =>
            {
                if (!Enum.TryParse<StockAdjustmentType>(
                        command.AdjustmentType,
                        ignoreCase: true,
                        out var adjustmentType))
                {
                    return true;
                }

                return adjustmentType == StockAdjustmentType.Set ||
                       command.Quantity > 0;
            })
            .WithMessage("Quantity must be greater than zero for Increase or Decrease adjustment.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidAdjustmentType(string value)
    {
        return Enum.TryParse<StockAdjustmentType>(value, ignoreCase: true, out _);
    }
}
```

***

# 7. Validator — Dashboard Query

Create folder kalau belum ada:

```text
src/OrderManagement.Application/Validators/Dashboard
```

Create file:

```text
src/OrderManagement.Application/Validators/Dashboard/BackofficeDashboardSummaryQueryDtoValidator.cs
```

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Dashboard;

namespace OrderManagement.Application.Validators.Dashboard;

public sealed class BackofficeDashboardSummaryQueryDtoValidator
    : AbstractValidator<BackofficeDashboardSummaryQueryDto>
{
    public BackofficeDashboardSummaryQueryDtoValidator()
    {
        RuleFor(query => query.LowStockThreshold)
            .InclusiveBetween(0, 100_000)
            .WithMessage("Low stock threshold must be between 0 and 100000.");
    }
}
```

***

# 8. Update Product Management Interfaces

## 8.1 `IProductManagementService.cs`

File:

```text
src/OrderManagement.Application/Abstractions/Products/IProductManagementService.cs
```

Tambahkan method:

```csharp
Task<AdjustProductStockResult> AdjustStockAsync(
    AdjustProductStockCommand command,
    CancellationToken cancellationToken = default);
```

Full interface:

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Abstractions.Products;

public interface IProductManagementService
{
    Task<PagedResult<BackofficeProductDto>> ListAsync(
        BackofficeProductListQueryDto query,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> GetByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> CreateAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> UpdateAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> SetStatusAsync(
        SetProductStatusCommand command,
        CancellationToken cancellationToken = default);

    Task<UploadProductImageResult> UploadImageAsync(
        UploadProductImageCommand command,
        CancellationToken cancellationToken = default);

    Task<AdjustProductStockResult> AdjustStockAsync(
        AdjustProductStockCommand command,
        CancellationToken cancellationToken = default);
}
```

***

## 8.2 `IProductManagementRepository.cs`

File:

```text
src/OrderManagement.Application/Abstractions/Repositories/IProductManagementRepository.cs
```

Tambahkan:

```csharp
Task<AdjustProductStockResult> AdjustStockAsync(
    AdjustProductStockPersistenceRequest request,
    CancellationToken cancellationToken = default);
```

***

# 9. Dashboard Service Abstractions

Create file:

```text
src/OrderManagement.Application/Abstractions/Dashboard/IBackofficeDashboardService.cs
```

```csharp
using OrderManagement.Application.DTOs.Dashboard;

namespace OrderManagement.Application.Abstractions.Dashboard;

public interface IBackofficeDashboardService
{
    Task<BackofficeDashboardSummaryDto> GetSummaryAsync(
        BackofficeDashboardSummaryQueryDto query,
        CancellationToken cancellationToken = default);
}
```

***

Create file:

```text
src/OrderManagement.Application/Abstractions/Repositories/IBackofficeDashboardRepository.cs
```

```csharp
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
```

***

# 10. ProductManagementService Update

File:

```text
src/OrderManagement.Application/Services/ProductManagementService.cs
```

Tambahkan field:

```csharp
private readonly IValidator<AdjustProductStockCommand> _stockAdjustmentValidator;
```

Update constructor parameter:

```csharp
IValidator<AdjustProductStockCommand> stockAdjustmentValidator,
```

Set field:

```csharp
_stockAdjustmentValidator = stockAdjustmentValidator;
```

***

## Add Method `AdjustStockAsync`

Tambahkan di class:

```csharp
public async Task<AdjustProductStockResult> AdjustStockAsync(
    AdjustProductStockCommand command,
    CancellationToken cancellationToken = default)
{
    var validationResult = await _stockAdjustmentValidator.ValidateAsync(command, cancellationToken);

    if (!validationResult.IsValid)
    {
        throw new ValidationAppException(
            "Adjust product stock request validation failed.",
            validationResult.Errors
                .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                .ToArray());
    }

    var existing = await GetByIdAsync(command.ProductId, cancellationToken);

    await _storeAuthorizationService.EnsureCanOperateStoreAsync(
        existing.StoreId,
        cancellationToken);

    var currentUserId = _currentUserContext.UserId
        ?? throw new UnauthorizedAppException("Authentication is required.");

    var adjustmentType = Enum.Parse<StockAdjustmentType>(
        command.AdjustmentType,
        ignoreCase: true);

    var result = await _repository.AdjustStockAsync(
        new AdjustProductStockPersistenceRequest
        {
            ProductId = command.ProductId,
            AdjustmentType = adjustmentType,
            Quantity = command.Quantity,
            ExpectedRowVersion = command.ExpectedRowVersion,
            Reason = string.IsNullOrWhiteSpace(command.Reason)
                ? null
                : command.Reason.Trim(),
            AdjustedBy = currentUserId,
            Now = _clock.UtcNow
        },
        cancellationToken);

    _activityLogWriter.TryWrite(
        ActivityLogTypes.ProductStockAdjusted,
        productId: result.ProductId,
        beforeState: new
        {
            stockQuantity = result.StockBefore,
            rowVersion = command.ExpectedRowVersion
        },
        afterState: new
        {
            stockQuantity = result.StockAfter,
            rowVersion = result.RowVersion
        },
        metadata: new
        {
            result.StoreId,
            result.Sku,
            result.Name,
            result.AdjustmentType,
            result.Quantity,
            command.Reason,
            adjustedBy = currentUserId
        });

    _logger.LogInformation(
        "Product stock adjusted. ProductId={ProductId} StoreId={StoreId} AdjustmentType={AdjustmentType} Quantity={Quantity} StockBefore={StockBefore} StockAfter={StockAfter}",
        result.ProductId,
        result.StoreId,
        result.AdjustmentType,
        result.Quantity,
        result.StockBefore,
        result.StockAfter);

    return result;
}
```

Pastikan using:

```csharp
using OrderManagement.Domain.Enums;
```

***

# 11. ProductManagementRepository Update

File:

```text
src/OrderManagement.Infrastructure/Repositories/ProductManagementRepository.cs
```

Tambahkan using jika belum:

```csharp
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
```

Tambahkan method:

```csharp
public async Task<AdjustProductStockResult> AdjustStockAsync(
    AdjustProductStockPersistenceRequest request,
    CancellationToken cancellationToken = default)
{
    await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

    try
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                "SET LOCAL lock_timeout = '5s';",
                transaction: transaction,
                cancellationToken: cancellationToken));

        var product = await connection.QuerySingleOrDefaultAsync<LockedProductForAdjustmentRow>(
            new CommandDefinition(
                """
                SELECT
                    p.id AS Id,
                    p.store_id AS StoreId,
                    p.sku AS Sku,
                    p.name AS Name,
                    p.stock_quantity AS StockQuantity,
                    p.row_version AS RowVersion
                FROM products p
                WHERE p.id = @ProductId
                FOR UPDATE;
                """,
                new { request.ProductId },
                transaction,
                cancellationToken: cancellationToken));

        if (product is null)
        {
            throw NotFoundAppException.Product(request.ProductId);
        }

        if (product.StoreId is null)
        {
            throw new ConflictAppException(
                ErrorCodes.ProductStoreNotAssigned,
                "Product is not assigned to a store.");
        }

        if (product.RowVersion != request.ExpectedRowVersion)
        {
            throw ConcurrencyAppException.RowVersionMismatch(
                request.ExpectedRowVersion,
                product.RowVersion);
        }

        var stockBefore = product.StockQuantity;

        var stockAfter = request.AdjustmentType switch
        {
            StockAdjustmentType.Increase => stockBefore + request.Quantity,
            StockAdjustmentType.Decrease => stockBefore - request.Quantity,
            StockAdjustmentType.Set => request.Quantity,
            _ => throw new InvalidOperationException($"Unsupported stock adjustment type {request.AdjustmentType}.")
        };

        if (stockAfter < 0)
        {
            throw new ConflictAppException(
                ErrorCodes.InvalidStockAdjustment,
                "Stock adjustment would make product stock negative.");
        }

        var nextRowVersion = product.RowVersion + 1;

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE products
                SET
                    stock_quantity = @StockAfter,
                    row_version = @NextRowVersion,
                    updated_at = @Now
                WHERE id = @ProductId
                  AND row_version = @CurrentRowVersion;
                """,
                new
                {
                    ProductId = product.Id,
                    StockAfter = stockAfter,
                    NextRowVersion = nextRowVersion,
                    CurrentRowVersion = product.RowVersion,
                    request.Now
                },
                transaction,
                cancellationToken: cancellationToken));

        if (affectedRows != 1)
        {
            throw new ConcurrencyAppException(
                "Product has been modified by another user. Please refresh and try again.");
        }

        var movementQuantity = Math.Abs(stockAfter - stockBefore);

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO inventory_movements
                    (id, product_id, order_id, movement_type, quantity,
                     stock_before, stock_after, reason, created_by, created_at)
                VALUES
                    (@Id, @ProductId, NULL, @MovementType, @Quantity,
                     @StockBefore, @StockAfter, @Reason, @CreatedBy, @Now);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    MovementType = InventoryMovementType.ManualAdjustment.ToString(),
                    Quantity = movementQuantity,
                    StockBefore = stockBefore,
                    StockAfter = stockAfter,
                    Reason = BuildAdjustmentReason(request.AdjustmentType, request.Reason),
                    CreatedBy = request.AdjustedBy,
                    request.Now
                },
                transaction,
                cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return new AdjustProductStockResult
        {
            ProductId = product.Id,
            StoreId = product.StoreId.Value,
            Sku = product.Sku,
            Name = product.Name,
            AdjustmentType = request.AdjustmentType.ToString(),
            Quantity = request.Quantity,
            StockBefore = stockBefore,
            StockAfter = stockAfter,
            RowVersion = nextRowVersion,
            UpdatedAt = request.Now
        };
    }
    catch
    {
        await transaction.RollbackAsync(cancellationToken);
        throw;
    }
}

private static string BuildAdjustmentReason(
    StockAdjustmentType adjustmentType,
    string? reason)
{
    var prefix = $"Manual stock adjustment: {adjustmentType}.";

    return string.IsNullOrWhiteSpace(reason)
        ? prefix
        : $"{prefix} Reason: {reason.Trim()}";
}

private sealed class LockedProductForAdjustmentRow
{
    public Guid Id { get; init; }

    public Guid? StoreId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public long RowVersion { get; init; }
}
```

***

# 12. Dashboard Service

Create file:

```text
src/OrderManagement.Application/Services/BackofficeDashboardService.cs
```

```csharp
using FluentValidation;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Dashboard;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Dashboard;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class BackofficeDashboardService : IBackofficeDashboardService
{
    private readonly IBackofficeDashboardRepository _repository;
    private readonly IStoreRepository _storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IClock _clock;
    private readonly IValidator<BackofficeDashboardSummaryQueryDto> _validator;

    public BackofficeDashboardService(
        IBackofficeDashboardRepository repository,
        IStoreRepository storeRepository,
        IStoreAuthorizationService storeAuthorizationService,
        ICurrentUserContext currentUserContext,
        IClock clock,
        IValidator<BackofficeDashboardSummaryQueryDto> validator)
    {
        _repository = repository;
        _storeRepository = storeRepository;
        _storeAuthorizationService = storeAuthorizationService;
        _currentUserContext = currentUserContext;
        _clock = clock;
        _validator = validator;
    }

    public async Task<BackofficeDashboardSummaryDto> GetSummaryAsync(
        BackofficeDashboardSummaryQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice dashboard query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        if (query.StoreId is not null)
        {
            await _storeAuthorizationService.EnsureCanOperateStoreAsync(
                query.StoreId.Value,
                cancellationToken);
        }

        var allowedStoreIds = await ResolveAllowedStoreIdsAsync(cancellationToken);

        return await _repository.GetSummaryAsync(
            query,
            allowedStoreIds,
            _clock.UtcNow,
            cancellationToken);
    }

    private async Task<IReadOnlyCollection<Guid>?> ResolveAllowedStoreIdsAsync(
        CancellationToken cancellationToken)
    {
        var role = _currentUserContext.Role
            ?? throw new UnauthorizedAppException("Authentication is required.");

        var userId = _currentUserContext.UserId
            ?? throw new UnauthorizedAppException("Authentication is required.");

        if (role == UserRole.ApplicationAdmin)
        {
            return null;
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            var stores = await _storeRepository.ListByUserMembershipAsync(userId, cancellationToken);

            return stores.Select(store => store.Id).ToArray();
        }

        throw new ForbiddenAppException("User is not allowed to access seller dashboard.");
    }
}
```

***

# 13. Dashboard Repository

Create file:

```text
src/OrderManagement.Infrastructure/Repositories/BackofficeDashboardRepository.cs
```

```csharp
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
            return Empty(query.StoreId, null, now);
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

        if (query.StoreId is not null)
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
                       WHERE (@StoreId::uuid IS NOT NULL AND s.id = @StoreId)
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

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var result = await connection.QuerySingleAsync<BackofficeDashboardSummaryDto>(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));

        return result;
    }

    private static BackofficeDashboardSummaryDto Empty(
        Guid? storeId,
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
```

***

# 14. Update Order Creation to Set `orders.store_id`

File:

```text
src/OrderManagement.Infrastructure/Repositories/OrderRepository.cs
```

## 14.1 Update `LockedProductRow`

Tambahkan:

```csharp
public Guid? StoreId { get; init; }
```

Final row:

```csharp
private sealed class LockedProductRow
{
    public Guid Id { get; init; }

    public Guid? StoreId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public long RowVersion { get; init; }

    public bool IsActive { get; init; }
}
```

## 14.2 Update product lock SELECT

Di `CreateAsync`, query product lock tambahkan `store_id`:

```sql
SELECT
    id AS Id,
    store_id AS StoreId,
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
```

## 14.3 Validate all products have same store

Setelah active product validation, tambahkan:

```csharp
var storeIds = lockedProducts
    .Select(product => product.StoreId)
    .Distinct()
    .ToArray();

if (storeIds.Any(storeId => storeId is null))
{
    throw new ConflictAppException(
        ErrorCodes.ProductStoreNotAssigned,
        "One or more products are not assigned to a store.");
}

if (storeIds.Length != 1)
{
    throw new ConflictAppException(
        ErrorCodes.MixedStoreOrderNotAllowed,
        "Order can only contain products from the same store.");
}

var orderStoreId = storeIds[0]!.Value;
```

## 14.4 Insert orders store\_id

Update insert order SQL:

```sql
INSERT INTO orders
    (id, order_number, store_id, customer_id, status, shipping_address, total_amount,
     row_version, created_by, updated_by, created_at, updated_at)
VALUES
    (@Id, @OrderNumber, @StoreId, @CustomerId, @Status, @ShippingAddress, @TotalAmount,
     @RowVersion, @CreatedBy, NULL, @Now, @Now);
```

Update parameters:

```csharp
new
{
    Id = request.OrderId,
    OrderNumber = orderNumber,
    StoreId = orderStoreId,
    request.CustomerId,
    Status = OrderStatus.Pending.ToString(),
    request.ShippingAddress,
    TotalAmount = totalAmount,
    RowVersion = 1L,
    request.CreatedBy,
    request.Now
}
```

> Dengan ini setiap order baru scoped ke satu store.

***

# 15. API Contracts

## 15.1 Stock Adjustment Request/Response

Create file:

```text
src/OrderManagement.Api/Contracts/Products/Backoffice/AdjustProductStockRequest.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed class AdjustProductStockRequest
{
    public string AdjustmentType { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }
}
```

***

Create file:

```text
src/OrderManagement.Api/Contracts/Products/Backoffice/AdjustProductStockResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed class AdjustProductStockResponse
{
    public Guid ProductId { get; init; }

    public Guid StoreId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string AdjustmentType { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public int StockBefore { get; init; }

    public int StockAfter { get; init; }

    public long RowVersion { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 15.2 Dashboard Response

Create folder:

```text
src/OrderManagement.Api/Contracts/Dashboard
```

Create file:

```text
src/OrderManagement.Api/Contracts/Dashboard/BackofficeDashboardSummaryQuery.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Dashboard;

public sealed class BackofficeDashboardSummaryQuery
{
    public Guid? StoreId { get; init; }

    public int LowStockThreshold { get; init; } = 5;
}
```

***

Create file:

```text
src/OrderManagement.Api/Contracts/Dashboard/BackofficeDashboardSummaryResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Dashboard;

public sealed class BackofficeDashboardSummaryResponse
{
    public Guid? StoreId { get; init; }

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
```

***

# 16. Update BackofficeProductsController

File:

```text
src/OrderManagement.Api/Controllers/BackofficeProductsController.cs
```

Tambahkan endpoint:

```csharp
[HttpPatch("{id:guid}/stock")]
[ProducesResponseType(typeof(AdjustProductStockResponse), StatusCodes.Status200OK)]
public async Task<ActionResult<AdjustProductStockResponse>> AdjustStock(
    Guid id,
    [FromBody] AdjustProductStockRequest request,
    CancellationToken cancellationToken)
{
    var result = await _productManagementService.AdjustStockAsync(
        new AdjustProductStockCommand
        {
            ProductId = id,
            AdjustmentType = request.AdjustmentType,
            Quantity = request.Quantity,
            ExpectedRowVersion = request.ExpectedRowVersion,
            Reason = request.Reason
        },
        cancellationToken);

    return Ok(new AdjustProductStockResponse
    {
        ProductId = result.ProductId,
        StoreId = result.StoreId,
        Sku = result.Sku,
        Name = result.Name,
        AdjustmentType = result.AdjustmentType,
        Quantity = result.Quantity,
        StockBefore = result.StockBefore,
        StockAfter = result.StockAfter,
        RowVersion = result.RowVersion,
        UpdatedAt = result.UpdatedAt
    });
}
```

Pastikan using:

```csharp
using OrderManagement.Application.DTOs.Products.Backoffice;
```

***

# 17. BackofficeDashboardController

Create file:

```text
src/OrderManagement.Api/Controllers/BackofficeDashboardController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Dashboard;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Dashboard;
using OrderManagement.Application.DTOs.Dashboard;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StoreBackofficeUser)]
[Route("api/v1/backoffice/dashboard")]
public sealed class BackofficeDashboardController : ControllerBase
{
    private readonly IBackofficeDashboardService _dashboardService;

    public BackofficeDashboardController(IBackofficeDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(BackofficeDashboardSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackofficeDashboardSummaryResponse>> GetSummary(
        [FromQuery] BackofficeDashboardSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetSummaryAsync(
            new BackofficeDashboardSummaryQueryDto
            {
                StoreId = query.StoreId,
                LowStockThreshold = query.LowStockThreshold
            },
            cancellationToken);

        return Ok(new BackofficeDashboardSummaryResponse
        {
            StoreId = result.StoreId,
            StoreName = result.StoreName,
            TotalProducts = result.TotalProducts,
            ActiveProducts = result.ActiveProducts,
            InactiveProducts = result.InactiveProducts,
            LowStockProducts = result.LowStockProducts,
            PendingOrders = result.PendingOrders,
            ConfirmedOrders = result.ConfirmedOrders,
            ShippedOrders = result.ShippedOrders,
            CancelledOrders = result.CancelledOrders,
            TodayOrders = result.TodayOrders,
            TodayRevenue = result.TodayRevenue,
            GeneratedAt = result.GeneratedAt
        });
    }
}
```

***

# 18. DI Updates

## 18.1 Application DI

File:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.Dashboard;
using OrderManagement.Application.DTOs.Dashboard;
using OrderManagement.Application.Validators.Dashboard;
```

Tambahkan services:

```csharp
services.AddScoped<IBackofficeDashboardService, BackofficeDashboardService>();
```

Tambahkan validators:

```csharp
services.AddScoped<IValidator<AdjustProductStockCommand>, AdjustProductStockCommandValidator>();
services.AddScoped<IValidator<BackofficeDashboardSummaryQueryDto>, BackofficeDashboardSummaryQueryDtoValidator>();
```

Pastikan already ada:

```csharp
services.AddScoped<IProductManagementService, ProductManagementService>();
```

***

## 18.2 Infrastructure DI

File:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Tambahkan:

```csharp
services.AddScoped<IBackofficeDashboardRepository, BackofficeDashboardRepository>();
```

***

# 19. Build

Run:

```bash
dotnet build
```

Kalau error `StoreBackofficeUser` tidak allow DevOps, itu memang benar.

Kalau error `orders.store_id` missing di DB, run API supaya migration apply:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

***

# 20. Manual Test

## 20.1 Login seller

```bash
SELLER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"selleradmin1","password":"Password123!"}')

SELLER_TOKEN=$(echo "$SELLER_LOGIN" | jq -r '.accessToken')
```

## 20.2 Get product row version

```bash
PRODUCT_ID=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT id FROM products WHERE sku = 'PRD-DEMO-001' LIMIT 1;" \
  | xargs)

ROW_VERSION=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT row_version FROM products WHERE id = '$PRODUCT_ID';" \
  | xargs)
```

## 20.3 Increase stock

```bash
curl -k -X PATCH "https://localhost:7000/api/v1/backoffice/products/$PRODUCT_ID/stock" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: stock-adjust-001" \
  -d "{
    \"adjustmentType\": \"Increase\",
    \"quantity\": 5,
    \"expectedRowVersion\": $ROW_VERSION,
    \"reason\": \"Manual restock from warehouse.\"
  }" | jq
```

Expected:

```json
{
  "adjustmentType": "Increase",
  "quantity": 5,
  "stockBefore": 25,
  "stockAfter": 30,
  "rowVersion": 2
}
```

## 20.4 Decrease too much

```bash
ROW_VERSION=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT row_version FROM products WHERE id = '$PRODUCT_ID';" \
  | xargs)

curl -k -i -X PATCH "https://localhost:7000/api/v1/backoffice/products/$PRODUCT_ID/stock" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"adjustmentType\": \"Decrease\",
    \"quantity\": 999999,
    \"expectedRowVersion\": $ROW_VERSION,
    \"reason\": \"Should fail.\"
  }"
```

Expected:

```text
409 INVALID_STOCK_ADJUSTMENT
```

## 20.5 Dashboard summary

```bash
curl -k -s "https://localhost:7000/api/v1/backoffice/dashboard/summary?lowStockThreshold=5" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq
```

Expected:

```json
{
  "totalProducts": 1,
  "activeProducts": 1,
  "lowStockProducts": 0,
  "pendingOrders": 0,
  "todayOrders": 0,
  "todayRevenue": 0
}
```

## 20.6 Activity logs

```bash
APPADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"appadmin","password":"Password123!"}')

APPADMIN_TOKEN=$(echo "$APPADMIN_LOGIN" | jq -r '.accessToken')

curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?correlationId=stock-adjust-001&page=1&pageSize=20" \
  -H "Authorization: Bearer $APPADMIN_TOKEN" | jq
```

Expected:

```text
ProductStockAdjusted
RequestCompleted
```

***

# 21. Security Acceptance

Harus:

```text
SellerAdmin can adjust stock only for own store product.
SellerOperator can adjust stock only for assigned store product.
ApplicationAdmin can adjust all products.
Buyer cannot access stock adjustment endpoint.
DevOps cannot access stock adjustment endpoint.
Manual adjustment inserts inventory_movements ManualAdjustment.
Stock cannot become negative.
RowVersion conflict rejects stale update.
Dashboard scoped to seller stores.
```

Tidak boleh:

```text
Seller adjust product from other store.
DevOps adjust business stock.
Buyer access dashboard.
Decrease stock below zero.
Dashboard leak other seller data.
Manual adjustment without inventory movement.
```

***

# 22. Commit

```bash
git add .
git commit -m "feat: add manual stock adjustment and seller dashboard summary"
```

***