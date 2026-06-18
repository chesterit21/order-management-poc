Siap bro, kita masuk **Batch 8: Create Order Transaction + Stock Row Lock**.

Ini batch paling penting karena mulai cover:

```text
Scenario A: Concurrent Stock Deduction
Scenario C: Idempotent Create Under Race
Double-click submit
Stock UI stale
Order insert + item insert + inventory movement + status history
```

> Catatan penting bro: untuk Batch 8 ini kita pakai idempotency service dari Batch 7 sebagai gate awal sebelum order transaction. Jadi request dengan key sama akan “ketahan” sebelum deduct stock. Deduct stock sendiri tetap di-protect oleh PostgreSQL row lock `FOR UPDATE ORDER BY id`.

***

# Batch 8 — Create Order Transaction + Stock Row Lock

## 0. Flow yang Kita Implement

Flow final:

```text
POST /api/v1/orders
    |
    |-- Require Idempotency-Key
    |-- Require JWT
    |-- Build CreateOrderCommand
    |-- Validate command
    |-- Compute request hash
    |-- Idempotency Begin
          |
          |-- Completed => return stored response
          |-- InProgress => 409
          |-- Different hash => 409
          |-- New => continue
    |
    |-- OrderRepository.CreateAsync()
          |
          |-- Begin transaction
          |-- Lock products FOR UPDATE ORDER BY id
          |-- Validate stock
          |-- Deduct stock
          |-- Insert order
          |-- Insert order_items
          |-- Insert inventory_movements
          |-- Insert order_status_history
          |-- Commit
    |
    |-- Mark idempotency Completed
    |
    |-- Return 201 Created
```

***

# 1. Application Order Abstraction

Buat folder kalau belum ada:

```bash
mkdir -p src/OrderManagement.Application/Abstractions/Orders
```

## `src/OrderManagement.Application/Abstractions/Orders/IOrderService.cs`

```csharp
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Application.Abstractions.Orders;

public interface IOrderService
{
    Task<CreateOrderOperationResult> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default);
}
```

***

# 2. Application Order DTOs

## 2.1 `CreateOrderItemCommand.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Orders/CreateOrderItemCommand.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class CreateOrderItemCommand
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }
}
```

***

## 2.2 `CreateOrderCommand.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Orders/CreateOrderCommand.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class CreateOrderCommand
{
    public required string IdempotencyKey { get; init; }

    public required string Endpoint { get; init; }

    public required Guid CustomerId { get; init; }

    public required IReadOnlyCollection<CreateOrderItemCommand> Items { get; init; }

    public required string ShippingAddress { get; init; }
}
```

***

## 2.3 `CreateOrderResult.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Orders/CreateOrderResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class CreateOrderResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Status { get; init; }

    public required string ShippingAddress { get; init; }

    public required decimal TotalAmount { get; init; }

    public required long RowVersion { get; init; }

    public required IReadOnlyCollection<CreateOrderItemResult> Items { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class CreateOrderItemResult
{
    public required Guid ProductId { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}
```

***

## 2.4 `CreateOrderOperationResult.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Orders/CreateOrderOperationResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class CreateOrderOperationResult
{
    private CreateOrderOperationResult(
        bool isStoredResponse,
        int statusCode,
        string? storedResponseBody,
        CreateOrderResult? response)
    {
        IsStoredResponse = isStoredResponse;
        StatusCode = statusCode;
        StoredResponseBody = storedResponseBody;
        Response = response;
    }

    public bool IsStoredResponse { get; }

    public int StatusCode { get; }

    public string? StoredResponseBody { get; }

    public CreateOrderResult? Response { get; }

    public static CreateOrderOperationResult Created(CreateOrderResult response)
    {
        return new CreateOrderOperationResult(
            false,
            201,
            null,
            response);
    }

    public static CreateOrderOperationResult Stored(int statusCode, string responseBody)
    {
        return new CreateOrderOperationResult(
            true,
            statusCode,
            responseBody,
            null);
    }
}
```

***

## 2.5 Persistence Request DTO

Create file:

```text
src/OrderManagement.Application/DTOs/Orders/CreateOrderPersistenceRequest.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class CreateOrderPersistenceRequest
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid CustomerId { get; init; }

    public required Guid CreatedBy { get; init; }

    public required string ShippingAddress { get; init; }

    public required IReadOnlyCollection<CreateOrderPersistenceItem> Items { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed class CreateOrderPersistenceItem
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }
}
```

***

# 3. Create Order Validator

## `src/OrderManagement.Application/Validators/Orders/CreateOrderCommandValidator.cs`

Replace:

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Application.Validators.Orders;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(command => command.IdempotencyKey)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Idempotency key is required.")
            .MaximumLength(200)
            .WithMessage("Idempotency key cannot be longer than 200 characters.");

        RuleFor(command => command.Endpoint)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Endpoint is required.")
            .MaximumLength(200)
            .WithMessage("Endpoint cannot be longer than 200 characters.");

        RuleFor(command => command.CustomerId)
            .NotEmpty()
            .WithMessage("Customer id is required.");

        RuleFor(command => command.ShippingAddress)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Shipping address is required.")
            .MaximumLength(1000)
            .WithMessage("Shipping address cannot be longer than 1000 characters.");

        RuleFor(command => command.Items)
            .NotEmpty()
            .WithMessage("Order must contain at least one item.");

        RuleFor(command => command.Items)
            .Must(items => items.Select(item => item.ProductId).Distinct().Count() == items.Count)
            .WithMessage("Duplicate product id is not allowed. Aggregate quantity per product before submitting.")
            .When(command => command.Items.Count > 0);

        RuleForEach(command => command.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.ProductId)
                    .NotEmpty()
                    .WithMessage("Product id is required.");

                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0)
                    .WithMessage("Quantity must be greater than zero.")
                    .LessThanOrEqualTo(100_000)
                    .WithMessage("Quantity is too large.");
            });
    }
}
```

***

# 4. Repository Abstraction

## `src/OrderManagement.Application/Abstractions/Repositories/IOrderRepository.cs`

Replace:

```csharp
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IOrderRepository
{
    Task<CreateOrderResult> CreateAsync(
        CreateOrderPersistenceRequest request,
        CancellationToken cancellationToken = default);
}
```

***

# 5. Order Service

## `src/OrderManagement.Application/Services/OrderService.cs`

Replace:

```csharp
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
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
    private readonly IValidator<CreateOrderCommand> _validator;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        ICurrentUserContext currentUserContext,
        IRequestHashService requestHashService,
        IIdempotencyService idempotencyService,
        IClock clock,
        IValidator<CreateOrderCommand> validator,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _currentUserContext = currentUserContext;
        _requestHashService = requestHashService;
        _idempotencyService = idempotencyService;
        _clock = clock;
        _validator = validator;
        _logger = logger;
    }

    public async Task<CreateOrderOperationResult> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Create order request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId is null)
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        var currentUserId = _currentUserContext.UserId.Value;
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
}
```

> Catatan bro: idempotency stored error di sini belum punya `correlationId` karena Application layer tidak tahu HTTP context. Tapi error realtime tetap akan dibungkus global exception lengkap correlation ID. Untuk retry dengan key yang sama setelah failure, user dapat stored failed response. Ini tradeoff POC yang masih clean enough.

***

# 6. Application DI Update

## `src/OrderManagement.Application/DependencyInjection.cs`

Replace:

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

        return services;
    }
}
```

***

# 7. Order Repository with Transaction + Row Lock

## `src/OrderManagement.Infrastructure/Repositories/OrderRepository.cs`

Replace:

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Abstractions.Repositories;
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
                    new
                    {
                        ProductIds = productIds
                    },
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

                await connection.ExecuteAsync(
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
}
```

***

# 8. Infrastructure DI Update

## `src/OrderManagement.Infrastructure/DependencyInjection.cs`

Pastikan ada registration `IOrderRepository`.

Replace file:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Infrastructure.Database;
using OrderManagement.Infrastructure.Idempotency;
using OrderManagement.Infrastructure.Options;
using OrderManagement.Infrastructure.Repositories;
using OrderManagement.Infrastructure.Rules;
using OrderManagement.Infrastructure.Security;
using OrderManagement.Infrastructure.Time;

namespace OrderManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.Configure<MigrationOptions>(
            configuration.GetSection(MigrationOptions.SectionName));

        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));

        services.Configure<IdempotencyOptions>(
            configuration.GetSection(IdempotencyOptions.SectionName));

        services.AddHttpContextAccessor();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        services.AddScoped<IDatabaseMigrationRunner, DatabaseMigrationRunner>();

        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        services.AddSingleton<IRequestHashService, RequestHashService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        services.AddSingleton<IOrderRulesService, NRulesOrderRulesService>();

        return services;
    }
}
```

***

# 9. API Request/Response Contracts

## 9.1 `CreateOrderItemRequest.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Orders/CreateOrderItemRequest.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Orders;

public sealed class CreateOrderItemRequest
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }
}
```

***

## 9.2 `CreateOrderRequest.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Orders/CreateOrderRequest.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Orders;

public sealed class CreateOrderRequest
{
    public Guid CustomerId { get; init; }

    public IReadOnlyCollection<CreateOrderItemRequest> Items { get; init; } = [];

    public string ShippingAddress { get; init; } = string.Empty;
}
```

***

## 9.3 `CreateOrderResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Orders/CreateOrderResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Orders;

public sealed class CreateOrderResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string ShippingAddress { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public long RowVersion { get; init; }

    public IReadOnlyCollection<CreateOrderItemResponse> Items { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CreateOrderItemResponse
{
    public Guid ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineTotal { get; init; }
}
```

***

# 10. OrdersController POST Endpoint

## `src/OrderManagement.Api/Controllers/OrdersController.cs`

Replace:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        var response = MapResponse(result.Response!);

        return CreatedAtAction(
            actionName: nameof(Create),
            routeValues: new { id = response.Id },
            value: response);
    }

    private static CreateOrderResponse MapResponse(CreateOrderResult result)
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
}
```

> `CreatedAtAction` sementara menunjuk ke action `Create` karena `GET /orders/{id}` belum ada di batch ini. Nanti Batch 9 kita ganti ke `nameof(GetById)`.

***

# 11. Build

Run:

```bash
dotnet build
```

Kalau sukses:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

***

# 12. Test Manual

## 12.1 Login

```bash
TOKEN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}' \
  | jq -r '.accessToken')
```

Ambil customer id:

```bash
CUSTOMER_ID=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}' \
  | jq -r '.user.id')
```

Ambil product id:

```bash
PRODUCT_ID=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT id FROM products WHERE sku = 'PRD-MOUSE-001' LIMIT 1;" \
  | xargs)
```

***

## 12.2 Create Order

```bash
IDEMPOTENCY_KEY=$(uuidgen)

curl -k -i -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -H "X-Correlation-ID: create-order-001" \
  -d "{
    \"customerId\": \"$CUSTOMER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 10
      }
    ],
    \"shippingAddress\": \"Jl. Example No. 1, Tangerang Selatan\"
  }"
```

Expected:

```http
201 Created
```

Body:

```json
{
  "id": "...",
  "orderNumber": "ORD-20260617-000001",
  "customerId": "...",
  "status": "Pending",
  "shippingAddress": "Jl. Example No. 1, Tangerang Selatan",
  "totalAmount": 1500000,
  "rowVersion": 1,
  "items": [
    {
      "productId": "...",
      "productName": "Mouse Wireless",
      "quantity": 10,
      "unitPrice": 150000,
      "lineTotal": 1500000
    }
  ],
  "createdAt": "..."
}
```

***

## 12.3 Retry Same Idempotency Key

Run request yang sama lagi dengan `Idempotency-Key` yang sama.

Expected:

```text
Return stored response
No second order
No second stock deduction
```

Cek stock:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT sku, stock_quantity FROM products WHERE sku = 'PRD-MOUSE-001';"
```

***

## 12.4 Same Key Different Payload

Run dengan key sama tapi quantity beda.

Expected:

```http
409 Conflict
```

Error:

```json
{
  "error": {
    "code": "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD",
    "message": "This idempotency key has already been used with a different request payload."
  }
}
```

***

## 12.5 Insufficient Stock

Kalau stock tinggal 5, request quantity 10.

Expected:

```http
409 Conflict
```

Error:

```json
{
  "error": {
    "code": "INSUFFICIENT_STOCK",
    "message": "Stock has changed. Product Mouse Wireless currently has only 5 units available.",
    "details": [
      {
        "field": "items.quantity",
        "message": "Requested quantity exceeds available stock.",
        "metadata": {
          "productId": "...",
          "requestedQuantity": 10,
          "availableQuantity": 5
        }
      }
    ]
  }
}
```

***

# 13. Concurrency Manual Test Sederhana

Reset stock mouse jadi 15 dulu:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "UPDATE products SET stock_quantity = 15 WHERE sku = 'PRD-MOUSE-001';"
```

Jalankan dua request parallel dengan key berbeda:

```bash
KEY1=$(uuidgen)
KEY2=$(uuidgen)

BODY="{
  \"customerId\": \"$CUSTOMER_ID\",
  \"items\": [
    {
      \"productId\": \"$PRODUCT_ID\",
      \"quantity\": 10
    }
  ],
  \"shippingAddress\": \"Jl. Concurrent Test\"
}"

curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $KEY1" \
  -d "$BODY" &

curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $KEY2" \
  -d "$BODY" &

wait
```

Expected:

```text
1 sukses
1 gagal INSUFFICIENT_STOCK
final stock = 5
stock tidak minus
```

Cek:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT sku, stock_quantity FROM products WHERE sku = 'PRD-MOUSE-001';"
```

***

# 14. Production Notes untuk Presentasi

Batch ini sudah cover:

```text
1. Idempotency key required untuk POST /orders.
2. Request hash mencegah key sama dipakai payload beda.
3. Concurrent same key dicegah unique constraint idempotency_keys.
4. Product rows dikunci dengan FOR UPDATE.
5. Lock ordering pakai ORDER BY id untuk mengurangi deadlock.
6. Stock divalidasi setelah row lock, bukan dari UI display.
7. Stock deduction dan order insert dalam satu transaction.
8. Inventory movement dicatat untuk audit.
9. Status history dicatat sejak order created.
10. Error stock stale mengembalikan availableQuantity.
```

Kalimat demo yang kuat:

> “UI stock hanya snapshot. Saat submit, backend lock row product, baca stock terbaru, lalu validasi. Kalau stock berubah karena race condition, user dapat pesan stock terbaru dan order ditolak tanpa membuat stock minus.”

***

# 15. Catatan Hardening Penting

Ada 1 hardening yang nanti bisa kita improve di Batch test/refactor:

```text
Idempotency Begin dan Order Transaction saat ini dua operasi terpisah.
```

Namun untuk POC ini, scenario C tetap aman karena:

```text
- idempotency insert terjadi sebelum order processing
- unique constraint mencegah dua request key sama menjadi owner
- request kedua akan dapat InProgress / stored response
```

Untuk production ultra-strict, kita bisa refactor ke:

```text
single transaction:
insert idempotency row
create order
mark completed
commit
```

Tapi implementasi saat ini sudah cukup kuat untuk POC dan presentasi, sambil tetap bisa dijelaskan tradeoff-nya.

***

# 16. Commit Batch 8

```bash
git add .
git commit -m "feat: add create order transaction with stock row locking"
```

***

