Gas bro, kita lanjut **Batch 16E — Store Order Access Refinement**.

Batch ini akan bikin seller panel order flow jadi proper:

```text
GET   /api/v1/backoffice/orders
GET   /api/v1/backoffice/orders/{id}
PATCH /api/v1/backoffice/orders/{id}/status
POST  /api/v1/backoffice/orders/{id}/cancel
```

Access final:

```text
Buyer:
  Pakai public /api/v1/orders untuk own orders.

SellerAdmin:
  Bisa akses order dari store miliknya.

SellerOperator:
  Bisa akses order dari store tempat dia aktif sebagai operator.

ApplicationAdmin:
  Bisa akses semua order.

DevOps:
  Tidak boleh akses business order endpoint.
  DevOps hanya logging/observability.
```

> Catatan bro: endpoint existing `/api/v1/orders` tetap buyer-facing. Endpoint baru `/api/v1/backoffice/orders` dipakai seller/admin panel.

***

# Batch 16E — Store Order Access Refinement

***

## 1. DTOs — Backoffice Orders

Buat folder:

```text
src/OrderManagement.Application/DTOs/Orders/Backoffice
```

***

## 1.1 `BackofficeOrderListQueryDto.cs`

```csharp
namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed class BackofficeOrderListQueryDto
{
    public Guid? StoreId { get; init; }

    public string? Status { get; init; }

    public Guid? CustomerId { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
```

***

## 1.2 `BackofficeOrderListItemDto.cs`

```csharp
namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed class BackofficeOrderListItemDto
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public required string Status { get; init; }

    public required decimal TotalAmount { get; init; }

    public required long RowVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 1.3 `BackofficeOrderDetailDto.cs`

```csharp
namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed class BackofficeOrderDetailDto
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public required string Status { get; init; }

    public required string ShippingAddress { get; init; }

    public required decimal TotalAmount { get; init; }

    public required long RowVersion { get; init; }

    public required IReadOnlyCollection<BackofficeOrderItemDto> Items { get; init; }

    public required IReadOnlyCollection<BackofficeOrderStatusHistoryDto> StatusHistory { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed class BackofficeOrderItemDto
{
    public required Guid ProductId { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}

public sealed class BackofficeOrderStatusHistoryDto
{
    public string? FromStatus { get; init; }

    public required string ToStatus { get; init; }

    public string? Reason { get; init; }

    public required Guid ChangedBy { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }
}
```

***

## 1.4 `BackofficeUpdateOrderStatusCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed class BackofficeUpdateOrderStatusCommand
{
    public required Guid OrderId { get; init; }

    public required string TargetStatus { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }
}
```

***

## 1.5 `BackofficeCancelOrderCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed class BackofficeCancelOrderCommand
{
    public required Guid OrderId { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? CancellationReason { get; init; }

    public string? Reason { get; init; }
}
```

***

## 1.6 `BackofficeOrderAccessDto.cs`

```csharp
namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed class BackofficeOrderAccessDto
{
    public required Guid OrderId { get; init; }

    public required Guid StoreId { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Status { get; init; }

    public required long RowVersion { get; init; }
}
```

***

# 2. Validator — Backoffice Orders

Create folder:

```text
src/OrderManagement.Application/Validators/Orders/Backoffice
```

***

## 2.1 `BackofficeOrderListQueryDtoValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders.Backoffice;

public sealed class BackofficeOrderListQueryDtoValidator : AbstractValidator<BackofficeOrderListQueryDto>
{
    public BackofficeOrderListQueryDtoValidator()
    {
        RuleFor(query => query.Status)
            .Must(BeValidStatus)
            .WithMessage("Status is invalid.")
            .When(query => !string.IsNullOrWhiteSpace(query.Status));

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Page size must be between 1 and 100.");

        RuleFor(query => query)
            .Must(query =>
                query.FromDate is null ||
                query.ToDate is null ||
                query.FromDate <= query.ToDate)
            .WithMessage("From date must be less than or equal to to date.");
    }

    private static bool BeValidStatus(string? status)
    {
        return Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _);
    }
}
```

***

## 2.2 `BackofficeUpdateOrderStatusCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders.Backoffice;

public sealed class BackofficeUpdateOrderStatusCommandValidator
    : AbstractValidator<BackofficeUpdateOrderStatusCommand>
{
    public BackofficeUpdateOrderStatusCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.TargetStatus)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Target status is required.")
            .Must(BeValidStatus)
            .WithMessage("Target status is invalid.")
            .Must(NotCancelled)
            .WithMessage("Use cancel endpoint to cancel an order.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidStatus(string? status)
    {
        return Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _);
    }

    private static bool NotCancelled(string? status)
    {
        return !Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) ||
               parsed != OrderStatus.Cancelled;
    }
}
```

***

## 2.3 `BackofficeCancelOrderCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders.Backoffice;

public sealed class BackofficeCancelOrderCommandValidator
    : AbstractValidator<BackofficeCancelOrderCommand>
{
    public BackofficeCancelOrderCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.CancellationReason)
            .Must(BeValidReason)
            .WithMessage("Cancellation reason is invalid.")
            .When(command => !string.IsNullOrWhiteSpace(command.CancellationReason));

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidReason(string? reason)
    {
        return Enum.TryParse<OrderCancellationReason>(reason, ignoreCase: true, out _);
    }
}
```

***

# 3. Repository Abstraction

Create file:

```text
src/OrderManagement.Application/Abstractions/Repositories/IBackofficeOrderRepository.cs
```

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders.Backoffice;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IBackofficeOrderRepository
{
    Task<PagedResult<BackofficeOrderListItemDto>> ListAsync(
        BackofficeOrderListQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        CancellationToken cancellationToken = default);

    Task<BackofficeOrderDetailDto?> GetDetailByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<BackofficeOrderAccessDto?> GetAccessAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}
```

***

# 4. Service Abstraction

Create file:

```text
src/OrderManagement.Application/Abstractions/Orders/IBackofficeOrderService.cs
```

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Orders.Backoffice;

namespace OrderManagement.Application.Abstractions.Orders;

public interface IBackofficeOrderService
{
    Task<PagedResult<BackofficeOrderListItemDto>> ListAsync(
        BackofficeOrderListQueryDto query,
        CancellationToken cancellationToken = default);

    Task<BackofficeOrderDetailDto> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<UpdateOrderStatusResult> UpdateStatusAsync(
        BackofficeUpdateOrderStatusCommand command,
        CancellationToken cancellationToken = default);

    Task<CancelOrderResult> CancelAsync(
        BackofficeCancelOrderCommand command,
        CancellationToken cancellationToken = default);
}
```

***

# 5. BackofficeOrderService

Create file:

```text
src/OrderManagement.Application/Services/BackofficeOrderService.cs
```

```csharp
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class BackofficeOrderService : IBackofficeOrderService
{
    private readonly IBackofficeOrderRepository _backofficeOrderRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IStoreRepository _storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService;
    private readonly IOrderCancellationPolicy _orderCancellationPolicy;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IClock _clock;
    private readonly IValidator<BackofficeOrderListQueryDto> _listValidator;
    private readonly IValidator<BackofficeUpdateOrderStatusCommand> _updateStatusValidator;
    private readonly IValidator<BackofficeCancelOrderCommand> _cancelValidator;
    private readonly IActivityLogWriter _activityLogWriter;
    private readonly ILogger<BackofficeOrderService> _logger;

    public BackofficeOrderService(
        IBackofficeOrderRepository backofficeOrderRepository,
        IOrderRepository orderRepository,
        IStoreRepository storeRepository,
        IStoreAuthorizationService storeAuthorizationService,
        IOrderCancellationPolicy orderCancellationPolicy,
        ICurrentUserContext currentUserContext,
        IClock clock,
        IValidator<BackofficeOrderListQueryDto> listValidator,
        IValidator<BackofficeUpdateOrderStatusCommand> updateStatusValidator,
        IValidator<BackofficeCancelOrderCommand> cancelValidator,
        IActivityLogWriter activityLogWriter,
        ILogger<BackofficeOrderService> logger)
    {
        _backofficeOrderRepository = backofficeOrderRepository;
        _orderRepository = orderRepository;
        _storeRepository = storeRepository;
        _storeAuthorizationService = storeAuthorizationService;
        _orderCancellationPolicy = orderCancellationPolicy;
        _currentUserContext = currentUserContext;
        _clock = clock;
        _listValidator = listValidator;
        _updateStatusValidator = updateStatusValidator;
        _cancelValidator = cancelValidator;
        _activityLogWriter = activityLogWriter;
        _logger = logger;
    }

    public async Task<PagedResult<BackofficeOrderListItemDto>> ListAsync(
        BackofficeOrderListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new BackofficeOrderListQueryDto
        {
            StoreId = query.StoreId,
            Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim(),
            CustomerId = query.CustomerId,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            Page = query.Page,
            PageSize = query.PageSize
        };

        var validationResult = await _listValidator.ValidateAsync(normalizedQuery, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice order list query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        if (normalizedQuery.StoreId is not null)
        {
            await _storeAuthorizationService.EnsureCanOperateStoreAsync(
                normalizedQuery.StoreId.Value,
                cancellationToken);
        }

        var allowedStoreIds = await ResolveAllowedStoreIdsAsync(cancellationToken);

        return await _backofficeOrderRepository.ListAsync(
            normalizedQuery,
            allowedStoreIds,
            cancellationToken);
    }

    public async Task<BackofficeOrderDetailDto> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Order id validation failed.",
                [AppErrorDetail.ForField("orderId", "Order id is required.")]);
        }

        var order = await _backofficeOrderRepository.GetDetailByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw NotFoundAppException.Order(orderId);
        }

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            order.StoreId,
            cancellationToken);

        return order;
    }

    public async Task<UpdateOrderStatusResult> UpdateStatusAsync(
        BackofficeUpdateOrderStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _updateStatusValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice update order status request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var access = await GetRequiredAccessAsync(command.OrderId, cancellationToken);

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            access.StoreId,
            cancellationToken);

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        if (currentRole == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot update business order status.");
        }

        var targetStatus = Enum.Parse<OrderStatus>(
            command.TargetStatus,
            ignoreCase: true);

        if (targetStatus == OrderStatus.Cancelled)
        {
            throw new BusinessRuleAppException(
                ErrorCodes.CancelledStatusRequiresCancelEndpoint,
                "Use cancel endpoint to cancel an order so stock and audit trail are handled correctly.");
        }

        _activityLogWriter.TryWrite(
            ActivityLogTypes.OrderStatusChangeStarted,
            orderId: command.OrderId,
            metadata: new
            {
                access.StoreId,
                targetStatus = targetStatus.ToString(),
                expectedRowVersion = command.ExpectedRowVersion,
                requestedBy = currentUserId,
                requestedByRole = currentRole.ToString(),
                source = "Backoffice"
            });

        var result = await _orderRepository.UpdateStatusAsync(
            new UpdateOrderStatusPersistenceRequest
            {
                OrderId = command.OrderId,
                TargetStatus = targetStatus,
                ExpectedRowVersion = command.ExpectedRowVersion,
                UpdatedBy = currentUserId,
                UpdatedByRole = currentRole,
                Reason = string.IsNullOrWhiteSpace(command.Reason)
                    ? null
                    : command.Reason.Trim(),
                Now = _clock.UtcNow
            },
            cancellationToken);

        _logger.LogInformation(
            "Backoffice order status updated. OrderId={OrderId} StoreId={StoreId} PreviousStatus={PreviousStatus} CurrentStatus={CurrentStatus} UpdatedBy={UpdatedBy}",
            result.Id,
            access.StoreId,
            result.PreviousStatus,
            result.CurrentStatus,
            currentUserId);

        return result;
    }

    public async Task<CancelOrderResult> CancelAsync(
        BackofficeCancelOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _cancelValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice cancel order request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var access = await GetRequiredAccessAsync(command.OrderId, cancellationToken);

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            access.StoreId,
            cancellationToken);

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        if (currentRole == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot cancel business order.");
        }

        var cancellationDecision = _orderCancellationPolicy.Resolve(
            command.CancellationReason,
            command.Reason,
            currentRole);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.OrderCancelStarted,
            orderId: command.OrderId,
            metadata: new
            {
                access.StoreId,
                cancellationReason = cancellationDecision.CancellationReason.ToString(),
                restoreStock = cancellationDecision.RestoreStock,
                requestedBy = currentUserId,
                requestedByRole = currentRole.ToString(),
                source = "Backoffice"
            });

        var result = await _orderRepository.CancelAsync(
            new CancelOrderPersistenceRequest
            {
                OrderId = command.OrderId,
                ExpectedRowVersion = command.ExpectedRowVersion,
                CancelledBy = currentUserId,
                CancelledByRole = currentRole,
                CancellationReason = cancellationDecision.CancellationReason,
                RestoreStock = cancellationDecision.RestoreStock,
                Reason = cancellationDecision.ReasonText,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _logger.LogInformation(
            "Backoffice order cancelled. OrderId={OrderId} StoreId={StoreId} PreviousStatus={PreviousStatus} CancelledBy={CancelledBy}",
            result.Id,
            access.StoreId,
            result.PreviousStatus,
            currentUserId);

        return result;
    }

    private async Task<BackofficeOrderAccessDto> GetRequiredAccessAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var access = await _backofficeOrderRepository.GetAccessAsync(orderId, cancellationToken);

        if (access is null)
        {
            throw NotFoundAppException.Order(orderId);
        }

        return access;
    }

    private async Task<IReadOnlyCollection<Guid>?> ResolveAllowedStoreIdsAsync(
        CancellationToken cancellationToken)
    {
        var role = GetRequiredCurrentUserRole();
        var userId = GetRequiredCurrentUserId();

        if (role == UserRole.ApplicationAdmin)
        {
            return null;
        }

        if (role == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot access business orders.");
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            var stores = await _storeRepository.ListByUserMembershipAsync(userId, cancellationToken);

            return stores
                .Select(store => store.Id)
                .ToArray();
        }

        throw new ForbiddenAppException("User is not allowed to access backoffice orders.");
    }

    private Guid GetRequiredCurrentUserId()
    {
        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId is null)
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        return _currentUserContext.UserId.Value;
    }

    private UserRole GetRequiredCurrentUserRole()
    {
        return _currentUserContext.Role
            ?? throw new ForbiddenAppException("User role claim is missing.");
    }
}
```

***

# 6. Repository Implementation

Create file:

```text
src/OrderManagement.Infrastructure/Repositories/BackofficeOrderRepository.cs
```

```csharp
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
```

***

# 7. API Contracts

Create folder:

```text
src/OrderManagement.Api/Contracts/Orders/Backoffice
```

***

## 7.1 `BackofficeOrderQuery.cs`

```csharp
namespace OrderManagement.Api.Contracts.Orders.Backoffice;

public sealed class BackofficeOrderQuery
{
    public Guid? StoreId { get; init; }

    public string? Status { get; init; }

    public Guid? CustomerId { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
```

***

## 7.2 `BackofficeOrderListItemResponse.cs`

```csharp
namespace OrderManagement.Api.Contracts.Orders.Backoffice;

public sealed class BackofficeOrderListItemResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public Guid StoreId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public long RowVersion { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 7.3 `BackofficeOrderDetailResponse.cs`

```csharp
namespace OrderManagement.Api.Contracts.Orders.Backoffice;

public sealed class BackofficeOrderDetailResponse
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

    public IReadOnlyCollection<BackofficeOrderItemResponse> Items { get; init; } = [];

    public IReadOnlyCollection<BackofficeOrderStatusHistoryResponse> StatusHistory { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class BackofficeOrderItemResponse
{
    public Guid ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineTotal { get; init; }
}

public sealed class BackofficeOrderStatusHistoryResponse
{
    public string? FromStatus { get; init; }

    public string ToStatus { get; init; } = string.Empty;

    public string? Reason { get; init; }

    public Guid ChangedBy { get; init; }

    public DateTimeOffset ChangedAt { get; init; }
}
```

***

## 7.4 `BackofficeUpdateOrderStatusRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Orders.Backoffice;

public sealed class BackofficeUpdateOrderStatusRequest
{
    public string TargetStatus { get; init; } = string.Empty;

    public long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }
}
```

***

## 7.5 `BackofficeCancelOrderRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Orders.Backoffice;

public sealed class BackofficeCancelOrderRequest
{
    public long ExpectedRowVersion { get; init; }

    public string? CancellationReason { get; init; }

    public string? Reason { get; init; }
}
```

***

# 8. BackofficeOrdersController

Create file:

```text
src/OrderManagement.Api/Controllers/BackofficeOrdersController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.Api.Contracts.Orders.Backoffice;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.DTOs.Orders.Backoffice;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StoreBackofficeUser)]
[Route("api/v1/backoffice/orders")]
public sealed class BackofficeOrdersController : ControllerBase
{
    private readonly IBackofficeOrderService _backofficeOrderService;

    public BackofficeOrdersController(IBackofficeOrderService backofficeOrderService)
    {
        _backofficeOrderService = backofficeOrderService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<BackofficeOrderListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<BackofficeOrderListItemResponse>>> List(
        [FromQuery] BackofficeOrderQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _backofficeOrderService.ListAsync(
            new BackofficeOrderListQueryDto
            {
                StoreId = query.StoreId,
                Status = query.Status,
                CustomerId = query.CustomerId,
                FromDate = query.FromDate,
                ToDate = query.ToDate,
                Page = query.Page,
                PageSize = query.PageSize
            },
            cancellationToken);

        return Ok(new PagedResponse<BackofficeOrderListItemResponse>
        {
            Items = result.Items.Select(MapListItem).ToArray(),
            Pagination = new PaginationResponse
            {
                Page = result.Page,
                PageSize = result.PageSize,
                TotalItems = result.TotalItems,
                TotalPages = result.TotalPages
            }
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BackofficeOrderDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackofficeOrderDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _backofficeOrderService.GetByIdAsync(id, cancellationToken);

        return Ok(MapDetail(result));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(UpdateOrderStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UpdateOrderStatusResponse>> UpdateStatus(
        Guid id,
        [FromBody] BackofficeUpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _backofficeOrderService.UpdateStatusAsync(
            new BackofficeUpdateOrderStatusCommand
            {
                OrderId = id,
                TargetStatus = request.TargetStatus,
                ExpectedRowVersion = request.ExpectedRowVersion,
                Reason = request.Reason
            },
            cancellationToken);

        return Ok(new UpdateOrderStatusResponse
        {
            Id = result.Id,
            OrderNumber = result.OrderNumber,
            PreviousStatus = result.PreviousStatus,
            CurrentStatus = result.CurrentStatus,
            RowVersion = result.RowVersion,
            UpdatedAt = result.UpdatedAt
        });
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(CancelOrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CancelOrderResponse>> Cancel(
        Guid id,
        [FromBody] BackofficeCancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _backofficeOrderService.CancelAsync(
            new BackofficeCancelOrderCommand
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

    private static BackofficeOrderListItemResponse MapListItem(BackofficeOrderListItemDto item)
    {
        return new BackofficeOrderListItemResponse
        {
            Id = item.Id,
            OrderNumber = item.OrderNumber,
            StoreId = item.StoreId,
            StoreName = item.StoreName,
            CustomerId = item.CustomerId,
            CustomerName = item.CustomerName,
            Status = item.Status,
            TotalAmount = item.TotalAmount,
            RowVersion = item.RowVersion,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    private static BackofficeOrderDetailResponse MapDetail(BackofficeOrderDetailDto item)
    {
        return new BackofficeOrderDetailResponse
        {
            Id = item.Id,
            OrderNumber = item.OrderNumber,
            StoreId = item.StoreId,
            StoreName = item.StoreName,
            CustomerId = item.CustomerId,
            CustomerName = item.CustomerName,
            Status = item.Status,
            ShippingAddress = item.ShippingAddress,
            TotalAmount = item.TotalAmount,
            RowVersion = item.RowVersion,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            Items = item.Items
                .Select(orderItem => new BackofficeOrderItemResponse
                {
                    ProductId = orderItem.ProductId,
                    ProductName = orderItem.ProductName,
                    Quantity = orderItem.Quantity,
                    UnitPrice = orderItem.UnitPrice,
                    LineTotal = orderItem.LineTotal
                })
                .ToArray(),
            StatusHistory = item.StatusHistory
                .Select(history => new BackofficeOrderStatusHistoryResponse
                {
                    FromStatus = history.FromStatus,
                    ToStatus = history.ToStatus,
                    Reason = history.Reason,
                    ChangedBy = history.ChangedBy,
                    ChangedAt = history.ChangedAt
                })
                .ToArray()
        };
    }
}
```

***

# 9. DI Updates

## 9.1 Application DI

File:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

Add using:

```csharp
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Application.Validators.Orders.Backoffice;
```

Add service:

```csharp
services.AddScoped<IBackofficeOrderService, BackofficeOrderService>();
```

Add validators:

```csharp
services.AddScoped<IValidator<BackofficeOrderListQueryDto>, BackofficeOrderListQueryDtoValidator>();
services.AddScoped<IValidator<BackofficeUpdateOrderStatusCommand>, BackofficeUpdateOrderStatusCommandValidator>();
services.AddScoped<IValidator<BackofficeCancelOrderCommand>, BackofficeCancelOrderCommandValidator>();
```

***

## 9.2 Infrastructure DI

File:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Add:

```csharp
services.AddScoped<IBackofficeOrderRepository, BackofficeOrderRepository>();
```

***

# 10. Update Existing OrdersController Access

Existing public `OrdersController` should be buyer-facing.

## 10.1 Change controller authorize

File:

```text
src/OrderManagement.Api/Controllers/OrdersController.cs
```

Keep authenticated:

```csharp
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
```

But service logic must enforce:

```text
Buyer / SellerAdmin buyer-like can use own buyer order endpoints.
SellerOperator cannot use buyer endpoints.
ApplicationAdmin can still inspect via public endpoint if needed or use backoffice.
DevOps cannot access business order endpoint.
```

***

# 11. Update OrderService Access Rules

File:

```text
src/OrderManagement.Application/Services/OrderService.cs
```

## 11.1 Create order

Replace old Customer check with:

```csharp
if (currentRole == UserRole.SellerOperator)
{
    throw new ForbiddenAppException("Seller operator cannot create buyer order.");
}

if (currentRole == UserRole.DevOps)
{
    throw new ForbiddenAppException("DevOps cannot create order.");
}

if (currentRole is UserRole.Buyer or UserRole.SellerAdmin &&
    command.CustomerId != currentUserId)
{
    throw new ForbiddenAppException("Buyer can only create order for themselves.");
}
```

***

## 11.2 Get order

Update `EnsureCanAccessOrder`:

```csharp
private void EnsureCanAccessOrder(Guid customerId)
{
    if (_currentUserContext.Role == UserRole.ApplicationAdmin)
    {
        return;
    }

    if (_currentUserContext.Role is UserRole.Buyer or UserRole.SellerAdmin &&
        _currentUserContext.UserId == customerId)
    {
        return;
    }

    throw new ForbiddenAppException("You do not have permission to access this order.");
}
```

***

## 11.3 List order

Update secured query role:

```csharp
var securedQuery = currentRole is UserRole.Buyer or UserRole.SellerAdmin
    ? new ListOrdersQueryDto
    {
        CustomerId = currentUserId,
        Page = normalizedQuery.Page,
        PageSize = normalizedQuery.PageSize,
        Status = normalizedQuery.Status,
        FromDate = normalizedQuery.FromDate,
        ToDate = normalizedQuery.ToDate
    }
    : currentRole == UserRole.ApplicationAdmin
        ? normalizedQuery
        : throw new ForbiddenAppException("You do not have permission to list buyer orders.");
```

This means:

```text
Buyer/SellerAdmin:
  Own buyer orders.

ApplicationAdmin:
  All buyer orders.

SellerOperator:
  Not via public endpoint. Use backoffice orders.

DevOps:
  No business order access.
```

***

## 11.4 UpdateStatus old public endpoint

Existing:

```http
PATCH /api/v1/orders/{id}/status
```

Should now be internal app admin only or eventually deprecated in favor of backoffice.

In `OrderService.UpdateStatusAsync`, enforce:

```csharp
if (currentRole != UserRole.ApplicationAdmin)
{
    throw new ForbiddenAppException("Only Application Admin can update order status from this endpoint. Seller users must use backoffice order endpoint.");
}
```

***

## 11.5 Cancel public endpoint

Buyer-facing cancel should allow only buyer-like own order.

Before calling repository, fetch order by id and check ownership already in cancel repository? Existing repository checks only if role Customer old. Update `OrderRepository.CancelAsync` role logic too.

***

# 12. Update OrderRepository Role Checks

File:

```text
src/OrderManagement.Infrastructure/Repositories/OrderRepository.cs
```

In `CancelAsync`, replace:

```csharp
if (request.CancelledByRole == UserRole.Customer &&
    order.CustomerId != request.CancelledBy)
```

with:

```csharp
if (request.CancelledByRole is UserRole.Buyer or UserRole.SellerAdmin &&
    order.CustomerId != request.CancelledBy)
{
    throw new ForbiddenAppException("Buyer can only cancel their own order.");
}
```

Replace allowed role block:

```csharp
if (request.CancelledByRole is not (UserRole.Customer or UserRole.Admin or UserRole.Ops))
```

with:

```csharp
if (request.CancelledByRole is not (
        UserRole.Buyer or
        UserRole.SellerAdmin or
        UserRole.SellerOperator or
        UserRole.ApplicationAdmin))
{
    throw new ForbiddenAppException("User is not allowed to cancel order.");
}
```

DevOps excluded.

***

# 13. Update NRules Role Logic

Wherever rule checks old roles:

```text
Customer
Admin
Ops
```

Update conceptually:

```text
Buyer/SellerAdmin:
  Buyer-side actions.

SellerAdmin/SellerOperator:
  Store-side actions.

ApplicationAdmin:
  Global business override.

DevOps:
  Never business mutation.
```

For payment rule:

```text
Allowed payment request roles:
Buyer, SellerAdmin, ApplicationAdmin
Not allowed:
SellerOperator, DevOps
```

For status transition:

```text
Allowed status update roles:
SellerAdmin, SellerOperator, ApplicationAdmin
Not allowed:
Buyer, DevOps
```

For cancel:

```text
Allowed cancel roles:
Buyer, SellerAdmin, SellerOperator, ApplicationAdmin
DevOps not allowed
```

If your `NRulesOrderRulesService` is hard-coded, update the facts evaluation accordingly.

***

# 14. Build

Run:

```bash
dotnet build
```

If old roles remain:

```bash
grep -R "UserRole.Customer\\|UserRole.Admin\\|UserRole.Ops" -n src tests
```

Replace:

```text
Customer -> Buyer
Admin -> ApplicationAdmin
Ops -> DevOps or SellerOperator depending context
```

***

# 15. Manual Test

## 15.1 Seller login

```bash
SELLER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"selleradmin1","password":"Password123!"}')

SELLER_TOKEN=$(echo "$SELLER_LOGIN" | jq -r '.accessToken')
```

## 15.2 List backoffice orders

```bash
curl -k -s "https://localhost:7000/api/v1/backoffice/orders?page=1&pageSize=20" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq
```

Expected:

```text
Only orders from seller's stores.
```

## 15.3 Get backoffice order detail

```bash
ORDER_ID="<order-id>"

curl -k -s "https://localhost:7000/api/v1/backoffice/orders/$ORDER_ID" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq
```

Expected:

```text
Allowed if order.store_id belongs to seller.
403 if other store.
```

## 15.4 Update status from backoffice

```bash
ROW_VERSION="<row-version>"

curl -k -X PATCH "https://localhost:7000/api/v1/backoffice/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"targetStatus\": \"Shipped\",
    \"expectedRowVersion\": $ROW_VERSION,
    \"reason\": \"Seller shipped the order.\"
  }" | jq
```

## 15.5 DevOps cannot access

```bash
DEVOPS_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"devops","password":"Password123!"}')

DEVOPS_TOKEN=$(echo "$DEVOPS_LOGIN" | jq -r '.accessToken')

curl -k -i "https://localhost:7000/api/v1/backoffice/orders?page=1&pageSize=20" \
  -H "Authorization: Bearer $DEVOPS_TOKEN"
```

Expected:

```text
403 Forbidden
```

***

# 16. Security Acceptance

Harus:

```text
SellerAdmin sees only own store orders.
SellerOperator sees only assigned store orders.
ApplicationAdmin sees all orders.
DevOps cannot access business orders.
Buyer cannot access backoffice orders.
Seller cannot update/cancel other seller order.
Backoffice order status update respects row_version.
Backoffice cancel respects stock restore/no-restore policy.
```

Tidak boleh:

```text
Seller sees other store order.
DevOps updates order.
Buyer accesses backoffice order endpoints.
Cancelled can be set via PATCH status.
Stale rowVersion update succeeds.
```

***

# 17. Commit

```bash
git add .
git commit -m "feat: add store-scoped backoffice order access"
```

***