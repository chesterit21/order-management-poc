Siap bro. Kita lanjut **Batch 9: Get/List Orders**.

Target endpoint:

```http
GET /api/v1/orders/{id}
GET /api/v1/orders?status=Pending&customerId=...&fromDate=2026-06-01&toDate=2026-06-17&page=1&pageSize=20
```

Yang kita cover:

```text
1. Order detail query.
2. Order list query dengan filter status/customer/date range.
3. Pagination.
4. Authorization filtering:
   - Customer hanya boleh lihat own orders.
   - Admin/Ops boleh lihat semua.
5. Status history included di detail.
6. Dapper query parameterized.
7. No SQL injection.
8. Consistent error handling.
9. Fix CreatedAtAction dari Batch 8.
```

***

# Batch 9 — Get/List Orders

## 1. Application DTOs

***

## 1.1 `ListOrdersQueryDto.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Orders/ListOrdersQueryDto.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class ListOrdersQueryDto
{
    public string? Status { get; init; }

    public Guid? CustomerId { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
```

***

## 1.2 `GetOrderResult.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Orders/GetOrderResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class GetOrderResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public required string Status { get; init; }

    public required string ShippingAddress { get; init; }

    public required decimal TotalAmount { get; init; }

    public required long RowVersion { get; init; }

    public required IReadOnlyCollection<OrderItemResult> Items { get; init; }

    public required IReadOnlyCollection<OrderStatusHistoryResult> StatusHistory { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed class OrderItemResult
{
    public required Guid ProductId { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}

public sealed class OrderStatusHistoryResult
{
    public string? FromStatus { get; init; }

    public required string ToStatus { get; init; }

    public string? Reason { get; init; }

    public required Guid ChangedBy { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }
}
```

***

## 1.3 `OrderListItemResult.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Orders/OrderListItemResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class OrderListItemResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

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

# 2. Validator List Orders

## `ListOrdersQueryValidator.cs`

Replace:

```text
src/OrderManagement.Application/Validators/Orders/ListOrdersQueryValidator.cs
```

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders;

public sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQueryDto>
{
    public ListOrdersQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(BeValidOrderStatus)
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

    private static bool BeValidOrderStatus(string? status)
    {
        return Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _);
    }
}
```

***

# 3. Order Service Interface Update

## `IOrderService.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Orders/IOrderService.cs
```

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Application.Abstractions.Orders;

public interface IOrderService
{
    Task<CreateOrderOperationResult> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<GetOrderResult> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
        CancellationToken cancellationToken = default);
}
```

***

# 4. Order Repository Interface Update

## `IOrderRepository.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Repositories/IOrderRepository.cs
```

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IOrderRepository
{
    Task<CreateOrderResult> CreateAsync(
        CreateOrderPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<GetOrderResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
        CancellationToken cancellationToken = default);
}
```

***

# 5. OrderService Update

Replace full file:

```text
src/OrderManagement.Application/Services/OrderService.cs
```

```csharp
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.ValueObjects;

namespace OrderManagement.Application.Services;

public sealed class OrderService : IOrderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IRequestHashService _requestHashService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IClock _clock;
    private readonly IValidator<CreateOrderCommand> _createValidator;
    private readonly IValidator<ListOrdersQueryDto> _listValidator;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        ICurrentUserContext currentUserContext,
        IRequestHashService requestHashService,
        IIdempotencyService idempotencyService,
        IClock clock,
        IValidator<CreateOrderCommand> createValidator,
        IValidator<ListOrdersQueryDto> listValidator,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _currentUserContext = currentUserContext;
        _requestHashService = requestHashService;
        _idempotencyService = idempotencyService;
        _clock = clock;
        _createValidator = createValidator;
        _listValidator = listValidator;
        _logger = logger;
    }

    public async Task<CreateOrderOperationResult> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _createValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Create order request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = _currentUserContext.Role;

        if (currentRole == UserRole.Customer && command.CustomerId != currentUserId)
        {
            throw new ForbiddenAppException("Customer can only create order for themselves.");
        }

        var requestHash = _requestHashService.ComputeHash(new
        {
            command.CustomerId,
            Items = command.Items
                .OrderBy(item => item.ProductId)
                .Select(item => new
                {
                    item.ProductId,
                    item.Quantity
                })
                .ToArray(),
            ShippingAddress = command.ShippingAddress.Trim()
        });

        var idempotencyResult = await _idempotencyService.BeginAsync(
            command.IdempotencyKey,
            currentUserId,
            command.Endpoint,
            requestHash,
            cancellationToken);

        if (idempotencyResult.HasStoredResponse)
        {
            return CreateOrderOperationResult.Stored(
                idempotencyResult.StoredStatusCode!.Value,
                idempotencyResult.StoredResponseBody!);
        }

        var orderId = Guid.NewGuid();
        var now = _clock.UtcNow;

        // POC sequence. Production-grade alternative: database sequence or dedicated order number table.
        var orderNumber = OrderNumber.Generate(now, Random.Shared.NextInt64(1, 999999)).Value;

        try
        {
            var createResult = await _orderRepository.CreateAsync(
                new CreateOrderPersistenceRequest
                {
                    OrderId = orderId,
                    OrderNumber = orderNumber,
                    CustomerId = command.CustomerId,
                    CreatedBy = currentUserId,
                    ShippingAddress = command.ShippingAddress.Trim(),
                    Items = command.Items
                        .Select(item => new CreateOrderPersistenceItem
                        {
                            ProductId = item.ProductId,
                            Quantity = item.Quantity
                        })
                        .ToArray(),
                    Now = now
                },
                cancellationToken);

            var responseBody = JsonSerializer.Serialize(createResult, JsonOptions);

            await _idempotencyService.MarkCompletedAsync(
                idempotencyResult.RecordId!.Value,
                201,
                responseBody,
                "Order",
                createResult.Id,
                cancellationToken);

            _logger.LogInformation(
                "Order created successfully. OrderId={OrderId} OrderNumber={OrderNumber} CustomerId={CustomerId}",
                createResult.Id,
                createResult.OrderNumber,
                createResult.CustomerId);

            return CreateOrderOperationResult.Created(createResult);
        }
        catch (AppException exception)
        {
            var errorBody = JsonSerializer.Serialize(
                new
                {
                    error = new
                    {
                        code = exception.Code,
                        message = exception.Message,
                        details = exception.Details
                    }
                },
                JsonOptions);

            if (idempotencyResult.RecordId is not null)
            {
                await _idempotencyService.MarkFailedAsync(
                    idempotencyResult.RecordId.Value,
                    exception.StatusCode,
                    errorBody,
                    cancellationToken);
            }

            throw;
        }
    }

    public async Task<GetOrderResult> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationAppException(
                "Order id validation failed.",
                [AppErrorDetail.ForField("id", "Order id is required.")]);
        }

        var order = await _orderRepository.GetByIdAsync(id, cancellationToken);

        if (order is null)
        {
            throw NotFoundAppException.Order(id);
        }

        EnsureCanAccessOrder(order.CustomerId);

        return order;
    }

    public async Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new ListOrdersQueryDto
        {
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
                "Order list query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();

        var securedQuery = _currentUserContext.Role == UserRole.Customer
            ? normalizedQuery withCustomer(currentUserId)
            : normalizedQuery;

        _logger.LogDebug(
            "Listing orders. Status={Status} CustomerId={CustomerId} FromDate={FromDate} ToDate={ToDate} Page={Page} PageSize={PageSize}",
            securedQuery.Status,
            securedQuery.CustomerId,
            securedQuery.FromDate,
            securedQuery.ToDate,
            securedQuery.Page,
            securedQuery.PageSize);

        return await _orderRepository.ListAsync(securedQuery, cancellationToken);

        static ListOrdersQueryDto withCustomer(Guid customerId)
        {
            return new ListOrdersQueryDto
            {
                CustomerId = customerId,
                Page = normalizedQuery.Page,
                PageSize = normalizedQuery.PageSize,
                Status = normalizedQuery.Status,
                FromDate = normalizedQuery.FromDate,
                ToDate = normalizedQuery.ToDate
            };
        }
    }

    private Guid GetRequiredCurrentUserId()
    {
        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId is null)
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        return _currentUserContext.UserId.Value;
    }

    private void EnsureCanAccessOrder(Guid customerId)
    {
        if (_currentUserContext.IsAdminOrOps())
        {
            return;
        }

        if (_currentUserContext.Role == UserRole.Customer &&
            _currentUserContext.UserId == customerId)
        {
            return;
        }

        throw new ForbiddenAppException("You do not have permission to access this order.");
    }
}
```

## Penting: Ada compile issue di local function

C# local function di atas tidak bisa capture `normalizedQuery` kalau signature tanpa parameter dalam style tersebut? Bisa, tapi naming `withCustomer` lowercase tidak sesuai style dan lebih aman kita ganti potongan `securedQuery`.

**Ganti bagian ini:**

```csharp
var securedQuery = _currentUserContext.Role == UserRole.Customer
    ? normalizedQuery withCustomer(currentUserId)
    : normalizedQuery;
```

Itu salah syntax. Pakai versi final ini:

```csharp
var securedQuery = _currentUserContext.Role == UserRole.Customer
    ? new ListOrdersQueryDto
    {
        CustomerId = currentUserId,
        Page = normalizedQuery.Page,
        PageSize = normalizedQuery.PageSize,
        Status = normalizedQuery.Status,
        FromDate = normalizedQuery.FromDate,
        ToDate = normalizedQuery.ToDate
    }
    : normalizedQuery;
```

Dan hapus local function `withCustomer`.

Jadi bagian `ListAsync` final adalah:

```csharp
    public async Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new ListOrdersQueryDto
        {
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
                "Order list query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();

        var securedQuery = _currentUserContext.Role == UserRole.Customer
            ? new ListOrdersQueryDto
            {
                CustomerId = currentUserId,
                Page = normalizedQuery.Page,
                PageSize = normalizedQuery.PageSize,
                Status = normalizedQuery.Status,
                FromDate = normalizedQuery.FromDate,
                ToDate = normalizedQuery.ToDate
            }
            : normalizedQuery;

        _logger.LogDebug(
            "Listing orders. Status={Status} CustomerId={CustomerId} FromDate={FromDate} ToDate={ToDate} Page={Page} PageSize={PageSize}",
            securedQuery.Status,
            securedQuery.CustomerId,
            securedQuery.FromDate,
            securedQuery.ToDate,
            securedQuery.Page,
            securedQuery.PageSize);

        return await _orderRepository.ListAsync(securedQuery, cancellationToken);
    }
```

***

# 6. Application DI Update

## `DependencyInjection.cs`

Replace:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Application.Services;
using OrderManagement.Application.Validators.Auth;
using OrderManagement.Application.Validators.Orders;
using OrderManagement.Application.Validators.Products;

namespace OrderManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();

        services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
        services.AddScoped<IValidator<ProductListQueryDto>, ProductListQueryDtoValidator>();
        services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
        services.AddScoped<IValidator<ListOrdersQueryDto>, ListOrdersQueryValidator>();

        return services;
    }
}
```

***

# 7. Repository Implementation Update

Replace full file:

```text
src/OrderManagement.Infrastructure/Repositories/OrderRepository.cs
```

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OrderRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<CreateOrderResult> CreateAsync(
        CreateOrderPersistenceRequest request,
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

            var requestedItems = request.Items
                .GroupBy(item => item.ProductId)
                .Select(group => new
                {
                    ProductId = group.Key,
                    Quantity = group.Sum(x => x.Quantity)
                })
                .OrderBy(item => item.ProductId)
                .ToArray();

            var productIds = requestedItems
                .Select(item => item.ProductId)
                .ToArray();

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

            if (lockedProducts.Count != requestedItems.Length)
            {
                var foundIds = lockedProducts.Select(product => product.Id).ToHashSet();
                var missingProductId = productIds.First(id => !foundIds.Contains(id));

                throw NotFoundAppException.Product(missingProductId);
            }

            foreach (var product in lockedProducts)
            {
                if (!product.IsActive)
                {
                    throw NotFoundAppException.Product(product.Id);
                }
            }

            var productById = lockedProducts.ToDictionary(product => product.Id);

            foreach (var item in requestedItems)
            {
                var product = productById[item.ProductId];

                if (product.StockQuantity < item.Quantity)
                {
                    throw ConflictAppException.InsufficientStock(
                        product.Id,
                        product.Name,
                        item.Quantity,
                        product.StockQuantity,
                        "items.quantity");
                }
            }

            var orderItems = requestedItems
                .Select(item =>
                {
                    var product = productById[item.ProductId];
                    var lineTotal = product.Price * item.Quantity;

                    return new OrderItemInsertRow
                    {
                        Id = Guid.NewGuid(),
                        OrderId = request.OrderId,
                        ProductId = product.Id,
                        ProductNameSnapshot = product.Name,
                        UnitPriceSnapshot = product.Price,
                        Quantity = item.Quantity,
                        LineTotal = lineTotal
                    };
                })
                .ToArray();

            var totalAmount = orderItems.Sum(item => item.LineTotal);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO orders
                        (id, order_number, customer_id, status, shipping_address, total_amount,
                         row_version, created_by, updated_by, created_at, updated_at)
                    VALUES
                        (@Id, @OrderNumber, @CustomerId, @Status, @ShippingAddress, @TotalAmount,
                         @RowVersion, @CreatedBy, NULL, @Now, @Now);
                    """,
                    new
                    {
                        Id = request.OrderId,
                        request.OrderNumber,
                        request.CustomerId,
                        Status = OrderStatus.Pending.ToString(),
                        request.ShippingAddress,
                        TotalAmount = totalAmount,
                        RowVersion = 1L,
                        request.CreatedBy,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            foreach (var item in requestedItems)
            {
                var product = productById[item.ProductId];
                var stockBefore = product.StockQuantity;
                var stockAfter = stockBefore - item.Quantity;

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
                        "CONCURRENT_STOCK_UPDATE_CONFLICT",
                        "Product stock was modified concurrently. Please retry.");
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
                            OrderId = request.OrderId,
                            MovementType = InventoryMovementType.OrderCreatedDeduction.ToString(),
                            Quantity = item.Quantity,
                            StockBefore = stockBefore,
                            StockAfter = stockAfter,
                            Reason = "Stock deducted when order was created.",
                            request.CreatedBy,
                            request.Now
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            foreach (var item in orderItems)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO order_items
                            (id, order_id, product_id, product_name_snapshot,
                             unit_price_snapshot, quantity, line_total, created_at)
                        VALUES
                            (@Id, @OrderId, @ProductId, @ProductNameSnapshot,
                             @UnitPriceSnapshot, @Quantity, @LineTotal, @Now);
                        """,
                        new
                        {
                            item.Id,
                            item.OrderId,
                            item.ProductId,
                            item.ProductNameSnapshot,
                            item.UnitPriceSnapshot,
                            item.Quantity,
                            item.LineTotal,
                            request.Now
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO order_status_history
                        (id, order_id, from_status, to_status, reason, changed_by, created_at)
                    VALUES
                        (@Id, @OrderId, NULL, @ToStatus, @Reason, @ChangedBy, @Now);
                    """,
                    new
                    {
                        Id = Guid.NewGuid(),
                        OrderId = request.OrderId,
                        ToStatus = OrderStatus.Pending.ToString(),
                        Reason = "Order created.",
                        ChangedBy = request.CreatedBy,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            return new CreateOrderResult
            {
                Id = request.OrderId,
                OrderNumber = request.OrderNumber,
                CustomerId = request.CustomerId,
                Status = OrderStatus.Pending.ToString(),
                ShippingAddress = request.ShippingAddress,
                TotalAmount = totalAmount,
                RowVersion = 1,
                CreatedAt = request.Now,
                Items = orderItems
                    .Select(item => new CreateOrderItemResult
                    {
                        ProductId = item.ProductId,
                        ProductName = item.ProductNameSnapshot,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPriceSnapshot,
                        LineTotal = item.LineTotal
                    })
                    .ToArray()
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
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

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
                                WHERE o.id = @Id
                                LIMIT 1;
                                """;

        var order = await connection.QuerySingleOrDefaultAsync<OrderDetailRow>(
            new CommandDefinition(
                orderSql,
                new { Id = id },
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

        var items = (await connection.QueryAsync<OrderItemResult>(
            new CommandDefinition(
                itemsSql,
                new { OrderId = id },
                cancellationToken: cancellationToken))).AsList();

        var history = (await connection.QueryAsync<OrderStatusHistoryResult>(
            new CommandDefinition(
                historySql,
                new { OrderId = id },
                cancellationToken: cancellationToken))).AsList();

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
            Items = items,
            StatusHistory = history
        };
    }

    public async Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
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

        parameters.Add("PageSize", query.PageSize);
        parameters.Add("Offset", offset);

        var whereClause = conditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", conditions);

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

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                countSql,
                parameters,
                cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<OrderListItemResult>(
            new CommandDefinition(
                dataSql,
                parameters,
                cancellationToken: cancellationToken));

        return new PagedResult<OrderListItemResult>
        {
            Items = items.AsList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    private static string NormalizeStatus(string status)
    {
        return Enum.Parse<OrderStatus>(status, ignoreCase: true).ToString();
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
}
```

***

# 8. API Contracts

***

## 8.1 `OrderListQuery.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Orders/OrderListQuery.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Orders;

public sealed class OrderListQuery
{
    public string? Status { get; init; }

    public Guid? CustomerId { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
```

***

## 8.2 `OrderDetailResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Orders/OrderDetailResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Orders;

public sealed class OrderDetailResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string ShippingAddress { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public long RowVersion { get; init; }

    public IReadOnlyCollection<OrderItemResponse> Items { get; init; } = [];

    public IReadOnlyCollection<OrderStatusHistoryResponse> StatusHistory { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class OrderItemResponse
{
    public Guid ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineTotal { get; init; }
}

public sealed class OrderStatusHistoryResponse
{
    public string? FromStatus { get; init; }

    public string ToStatus { get; init; } = string.Empty;

    public string? Reason { get; init; }

    public Guid ChangedBy { get; init; }

    public DateTimeOffset ChangedAt { get; init; }
}
```

***

## 8.3 `OrderListItemResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Orders/OrderListItemResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Orders;

public sealed class OrderListItemResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

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

# 9. OrdersController Update

Replace full file:

```text
src/OrderManagement.Api/Controllers/OrdersController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.Api.Extensions;
using OrderManagement.Api.Filters;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[Route("api/v1/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    [RequireIdempotencyKeyFilter]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.CreateAsync(
            new CreateOrderCommand
            {
                IdempotencyKey = Request.GetRequiredIdempotencyKey(),
                Endpoint = Request.GetNormalizedEndpoint(),
                CustomerId = request.CustomerId,
                ShippingAddress = request.ShippingAddress,
                Items = request.Items
                    .Select(item => new CreateOrderItemCommand
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity
                    })
                    .ToArray()
            },
            cancellationToken);

        if (result.IsStoredResponse)
        {
            return new ContentResult
            {
                StatusCode = result.StatusCode,
                Content = result.StoredResponseBody,
                ContentType = "application/json"
            };
        }

        var response = MapCreateResponse(result.Response!);

        return CreatedAtAction(
            nameof(GetById),
            new { id = response.Id },
            response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.GetByIdAsync(id, cancellationToken);

        return Ok(MapDetailResponse(result));
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrderListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrderListItemResponse>>> List(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.ListAsync(
            new ListOrdersQueryDto
            {
                Status = query.Status,
                CustomerId = query.CustomerId,
                FromDate = query.FromDate,
                ToDate = query.ToDate,
                Page = query.Page,
                PageSize = query.PageSize
            },
            cancellationToken);

        return Ok(new PagedResponse<OrderListItemResponse>
        {
            Items = result.Items
                .Select(item => new OrderListItemResponse
                {
                    Id = item.Id,
                    OrderNumber = item.OrderNumber,
                    CustomerId = item.CustomerId,
                    CustomerName = item.CustomerName,
                    Status = item.Status,
                    TotalAmount = item.TotalAmount,
                    RowVersion = item.RowVersion,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt
                })
                .ToArray(),
            Pagination = new PaginationResponse
            {
                Page = result.Page,
                PageSize = result.PageSize,
                TotalItems = result.TotalItems,
                TotalPages = result.TotalPages
            }
        });
    }

    private static CreateOrderResponse MapCreateResponse(CreateOrderResult result)
    {
        return new CreateOrderResponse
        {
            Id = result.Id,
            OrderNumber = result.OrderNumber,
            CustomerId = result.CustomerId,
            Status = result.Status,
            ShippingAddress = result.ShippingAddress,
            TotalAmount = result.TotalAmount,
            RowVersion = result.RowVersion,
            CreatedAt = result.CreatedAt,
            Items = result.Items
                .Select(item => new CreateOrderItemResponse
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal
                })
                .ToArray()
        };
    }

    private static OrderDetailResponse MapDetailResponse(GetOrderResult result)
    {
        return new OrderDetailResponse
        {
            Id = result.Id,
            OrderNumber = result.OrderNumber,
            CustomerId = result.CustomerId,
            CustomerName = result.CustomerName,
            Status = result.Status,
            ShippingAddress = result.ShippingAddress,
            TotalAmount = result.TotalAmount,
            RowVersion = result.RowVersion,
            CreatedAt = result.CreatedAt,
            UpdatedAt = result.UpdatedAt,
            Items = result.Items
                .Select(item => new OrderItemResponse
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal
                })
                .ToArray(),
            StatusHistory = result.StatusHistory
                .Select(history => new OrderStatusHistoryResponse
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

# 10. Build

Run:

```bash
dotnet build
```

Kalau sukses:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

***

# 11. Manual Test

## 11.1 Login Admin

```bash
ADMIN_TOKEN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Password123!"}' \
  | jq -r '.accessToken')
```

***

## 11.2 Login Customer

```bash
CUSTOMER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}')

CUSTOMER_TOKEN=$(echo "$CUSTOMER_LOGIN" | jq -r '.accessToken')
CUSTOMER_ID=$(echo "$CUSTOMER_LOGIN" | jq -r '.user.id')
```

***

## 11.3 List Orders Admin

```bash
curl -k -i "https://localhost:7000/api/v1/orders?page=1&pageSize=20" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "X-Correlation-ID: list-orders-admin-001"
```

***

## 11.4 List Orders Customer

```bash
curl -k -i "https://localhost:7000/api/v1/orders?page=1&pageSize=20" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "X-Correlation-ID: list-orders-customer-001"
```

Expected:

```text
Customer hanya melihat order miliknya sendiri,
walaupun query customerId dikirim user lain.
```

***

## 11.5 List with Filter

```bash
curl -k -i "https://localhost:7000/api/v1/orders?status=Pending&customerId=$CUSTOMER_ID&page=1&pageSize=10" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

***

## 11.6 Get Order Detail

Ambil order id:

```bash
ORDER_ID=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT id FROM orders ORDER BY created_at DESC LIMIT 1;" \
  | xargs)
```

Hit endpoint:

```bash
curl -k -i "https://localhost:7000/api/v1/orders/$ORDER_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

Expected response include:

```json
{
  "id": "...",
  "orderNumber": "...",
  "customerId": "...",
  "customerName": "...",
  "status": "Pending",
  "items": [],
  "statusHistory": []
}
```

***

# 12. Security & Performance Notes

Batch 9 ini sudah memperhatikan:

```text
1. Semua order endpoint protected JWT.
2. Customer tidak bisa list order customer lain.
3. Customer tidak bisa get detail order customer lain.
4. Admin/Ops bisa akses semua order.
5. Query list pakai parameterized DynamicParameters.
6. Tidak ada user-controlled ORDER BY.
7. Pagination dibatasi max 100.
8. Detail query dipisah menjadi order/items/history agar mapping jelas dan tidak duplicate row explosion.
9. Status filter dinormalisasi ke enum string resmi.
10. Date range divalidasi di Application layer.
```

***

# 13. Commit Batch 9

```bash
git add .
git commit -m "feat: add get and list orders endpoints"
```

***
