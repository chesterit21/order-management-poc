Gas bro, kita lanjut **Batch 11: Payment Simple Flow**.

Target endpoint:

```http
POST /api/v1/orders/{orderId}/payments
GET  /api/v1/orders/{orderId}/payments
```

Yang kita cover:

```text
1. Payment transaction dengan order row lock FOR UPDATE.
2. Payment success mengubah Pending -> Confirmed.
3. Payment failed tidak mengubah status order.
4. Prevent duplicate Paid payment.
5. NRules payment validation.
6. Payment/cancel race protection karena payment dan cancel sama-sama lock order row.
7. Owner/Admin/Ops authorization.
8. Payment history/list endpoint.
```

> Catatan: table `payments` dan partial unique index `uq_payments_one_paid_per_order` sudah dibuat di migration sebelumnya, jadi batch ini tidak butuh migration baru.

***

# Batch 11 — Payment Simple Flow

***

# 1. Application Payment Abstraction

Buat folder kalau belum ada:

```bash
mkdir -p src/OrderManagement.Application/Abstractions/Payments
```

## `src/OrderManagement.Application/Abstractions/Payments/IPaymentService.cs`

```csharp
using OrderManagement.Application.DTOs.Payments;

namespace OrderManagement.Application.Abstractions.Payments;

public interface IPaymentService
{
    Task<CreatePaymentResult> CreateAsync(
        CreatePaymentCommand command,
        CancellationToken cancellationToken = default);

    Task<PaymentListResult> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}
```

***

# 2. Application Payment DTOs

## 2.1 `CreatePaymentCommand.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Payments/CreatePaymentCommand.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Payments;

public sealed class CreatePaymentCommand
{
    public required Guid OrderId { get; init; }

    public required string Provider { get; init; }

    public required string SimulateResult { get; init; }
}
```

***

## 2.2 `CreatePaymentResult.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Payments/CreatePaymentResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Payments;

public sealed class CreatePaymentResult
{
    public required Guid PaymentId { get; init; }

    public required Guid OrderId { get; init; }

    public required decimal Amount { get; init; }

    public required string Status { get; init; }

    public required string OrderStatus { get; init; }

    public required string Provider { get; init; }

    public string? PaymentReference { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
```

***

## 2.3 Create `PaymentListResult.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Payments/PaymentListResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Payments;

public sealed class PaymentListResult
{
    public required Guid OrderId { get; init; }

    public required IReadOnlyCollection<PaymentResult> Payments { get; init; }
}

public sealed class PaymentResult
{
    public required Guid Id { get; init; }

    public required decimal Amount { get; init; }

    public required string Status { get; init; }

    public required string Provider { get; init; }

    public string? PaymentReference { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 2.4 Create persistence request

Create file:

```text
src/OrderManagement.Application/DTOs/Payments/CreatePaymentPersistenceRequest.cs
```

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.DTOs.Payments;

public sealed class CreatePaymentPersistenceRequest
{
    public required Guid OrderId { get; init; }

    public required Guid RequestedBy { get; init; }

    public required UserRole RequestedByRole { get; init; }

    public required string Provider { get; init; }

    public required PaymentSimulationResult SimulateResult { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public enum PaymentSimulationResult
{
    Success = 1,
    Failed = 2
}
```

***

# 3. Payment Validator

## `src/OrderManagement.Application/Validators/Payments/CreatePaymentCommandValidator.cs`

Replace:

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Payments;

namespace OrderManagement.Application.Validators.Payments;

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.Provider)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Payment provider is required.")
            .MaximumLength(100)
            .WithMessage("Payment provider cannot be longer than 100 characters.");

        RuleFor(command => command.SimulateResult)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Simulate result is required.")
            .Must(BeValidSimulationResult)
            .WithMessage("Simulate result must be Success or Failed.");
    }

    private static bool BeValidSimulationResult(string value)
    {
        return Enum.TryParse<PaymentSimulationResult>(value, ignoreCase: true, out _);
    }
}
```

***

# 4. Payment Repository Abstraction

Create/replace:

```text
src/OrderManagement.Application/Abstractions/Repositories/IPaymentRepository.cs
```

```csharp
using OrderManagement.Application.DTOs.Payments;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IPaymentRepository
{
    Task<CreatePaymentResult> CreateAsync(
        CreatePaymentPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentListResult> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}
```

***

# 5. Payment Service

## `src/OrderManagement.Application/Services/PaymentService.cs`

Replace:

```csharp
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Payments;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IClock _clock;
    private readonly IValidator<CreatePaymentCommand> _validator;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        ICurrentUserContext currentUserContext,
        IClock clock,
        IValidator<CreatePaymentCommand> validator,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _currentUserContext = currentUserContext;
        _clock = clock;
        _validator = validator;
        _logger = logger;
    }

    public async Task<CreatePaymentResult> CreateAsync(
        CreatePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Create payment request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        var simulateResult = Enum.Parse<PaymentSimulationResult>(
            command.SimulateResult,
            ignoreCase: true);

        var result = await _paymentRepository.CreateAsync(
            new CreatePaymentPersistenceRequest
            {
                OrderId = command.OrderId,
                RequestedBy = currentUserId,
                RequestedByRole = currentRole,
                Provider = command.Provider.Trim(),
                SimulateResult = simulateResult,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _logger.LogInformation(
            "Payment created. PaymentId={PaymentId} OrderId={OrderId} PaymentStatus={PaymentStatus} OrderStatus={OrderStatus} RequestedBy={RequestedBy}",
            result.PaymentId,
            result.OrderId,
            result.Status,
            result.OrderStatus,
            currentUserId);

        return result;
    }

    public async Task<PaymentListResult> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Order id validation failed.",
                [AppErrorDetail.ForField("orderId", "Order id is required.")]);
        }

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw NotFoundAppException.Order(orderId);
        }

        EnsureCanAccessOrder(order.CustomerId);

        return await _paymentRepository.ListByOrderIdAsync(orderId, cancellationToken);
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

***

# 6. Application DI Update

## `src/OrderManagement.Application/DependencyInjection.cs`

Replace:

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Payments;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Application.Services;
using OrderManagement.Application.Validators.Auth;
using OrderManagement.Application.Validators.Orders;
using OrderManagement.Application.Validators.Payments;
using OrderManagement.Application.Validators.Products;

namespace OrderManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();

        services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
        services.AddScoped<IValidator<ProductListQueryDto>, ProductListQueryDtoValidator>();
        services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
        services.AddScoped<IValidator<ListOrdersQueryDto>, ListOrdersQueryValidator>();
        services.AddScoped<IValidator<UpdateOrderStatusCommand>, UpdateOrderStatusCommandValidator>();
        services.AddScoped<IValidator<CancelOrderCommand>, CancelOrderCommandValidator>();
        services.AddScoped<IValidator<CreatePaymentCommand>, CreatePaymentCommandValidator>();

        return services;
    }
}
```

***

# 7. Payment Repository with Order Row Lock

## `src/OrderManagement.Infrastructure/Repositories/PaymentRepository.cs`

Replace:

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IOrderRulesService _orderRulesService;

    public PaymentRepository(
        IDbConnectionFactory connectionFactory,
        IOrderRulesService orderRulesService)
    {
        _connectionFactory = connectionFactory;
        _orderRulesService = orderRulesService;
    }

    public async Task<CreatePaymentResult> CreateAsync(
        CreatePaymentPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

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

            if (request.RequestedByRole == UserRole.Customer &&
                order.CustomerId != request.RequestedBy)
            {
                throw new ForbiddenAppException("Customer can only pay their own order.");
            }

            if (request.RequestedByRole is not (UserRole.Customer or UserRole.Admin or UserRole.Ops))
            {
                throw new ForbiddenAppException("User is not allowed to create payment.");
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
                throw new BusinessRuleAppException(
                    ruleResult.ErrorCode ?? ErrorCodes.PaymentNotAllowed,
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
```

***

# 8. Infrastructure DI Update

## `src/OrderManagement.Infrastructure/DependencyInjection.cs`

Pastikan `IPaymentRepository` registered.

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
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

        services.AddSingleton<IRequestHashService, RequestHashService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        services.AddSingleton<IOrderRulesService, NRulesOrderRulesService>();

        return services;
    }
}
```

***

# 9. API Payment Contracts

## 9.1 `CreatePaymentRequest.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Payments/CreatePaymentRequest.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Payments;

public sealed class CreatePaymentRequest
{
    public string Provider { get; init; } = string.Empty;

    public string SimulateResult { get; init; } = string.Empty;
}
```

***

## 9.2 `CreatePaymentResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Payments/CreatePaymentResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Payments;

public sealed class CreatePaymentResponse
{
    public Guid PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public decimal Amount { get; init; }

    public string Status { get; init; } = string.Empty;

    public string OrderStatus { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? PaymentReference { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
```

***

## 9.3 `PaymentResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Payments/PaymentResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Payments;

public sealed class PaymentResponse
{
    public Guid Id { get; init; }

    public decimal Amount { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? PaymentReference { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 9.4 `PaymentListResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Payments/PaymentListResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Payments;

public sealed class PaymentListResponse
{
    public Guid OrderId { get; init; }

    public IReadOnlyCollection<PaymentResponse> Payments { get; init; } = [];
}
```

***

# 10. PaymentsController

## `src/OrderManagement.Api/Controllers/PaymentsController.cs`

Replace:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Payments;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Payments;
using OrderManagement.Application.DTOs.Payments;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[Route("api/v1/orders/{orderId:guid}/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreatePaymentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CreatePaymentResponse>> Create(
        Guid orderId,
        [FromBody] CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.CreateAsync(
            new CreatePaymentCommand
            {
                OrderId = orderId,
                Provider = request.Provider,
                SimulateResult = request.SimulateResult
            },
            cancellationToken);

        return Ok(new CreatePaymentResponse
        {
            PaymentId = result.PaymentId,
            OrderId = result.OrderId,
            Amount = result.Amount,
            Status = result.Status,
            OrderStatus = result.OrderStatus,
            Provider = result.Provider,
            PaymentReference = result.PaymentReference,
            CreatedAt = result.CreatedAt
        });
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaymentListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentListResponse>> List(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.ListByOrderIdAsync(
            orderId,
            cancellationToken);

        return Ok(new PaymentListResponse
        {
            OrderId = result.OrderId,
            Payments = result.Payments
                .Select(payment => new PaymentResponse
                {
                    Id = payment.Id,
                    Amount = payment.Amount,
                    Status = payment.Status,
                    Provider = payment.Provider,
                    PaymentReference = payment.PaymentReference,
                    CreatedAt = payment.CreatedAt,
                    UpdatedAt = payment.UpdatedAt
                })
                .ToArray()
        });
    }
}
```

***

# 11. Payment/Cancel Race Protection Explanation

Dengan implementasi ini:

```text
Payment flow:
1. Begin transaction.
2. Lock order row FOR UPDATE.
3. Validate status Pending.
4. Check existing Paid payment.
5. Insert payment.
6. If success, update order Pending -> Confirmed.
7. Commit.

Cancel flow dari Batch 10 V2:
1. Begin transaction.
2. Lock order row FOR UPDATE.
3. Validate cancel allowed.
4. Cancel order.
5. Commit.
```

Jadi kalau payment dan cancel datang bersamaan:

```text
Case A — Payment lock dulu:
- Payment sees Pending.
- Payment Paid.
- Order becomes Confirmed.
- Cancel waits.
- Cancel reads Confirmed after payment commit.
- Cancel still allowed.
- If payment Paid, cancel marks payment RefundRequired.

Case B — Cancel lock dulu:
- Cancel sees Pending.
- Cancel changes order to Cancelled.
- Payment waits.
- Payment reads Cancelled after cancel commit.
- NRules rejects payment with PAYMENT_NOT_ALLOWED.
```

Ini race-safe.

***

# 12. Build

Run:

```bash
dotnet build
```

Kalau sukses:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

***

# 13. Manual Test

## 13.1 Login customer

```bash
CUSTOMER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}')

CUSTOMER_TOKEN=$(echo "$CUSTOMER_LOGIN" | jq -r '.accessToken')
CUSTOMER_ID=$(echo "$CUSTOMER_LOGIN" | jq -r '.user.id')
```

***

## 13.2 Create order Pending

```bash
PRODUCT_ID=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT id FROM products WHERE sku = 'PRD-MOUSE-001' LIMIT 1;" \
  | xargs)

ORDER_RESPONSE=$(curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -d "{
    \"customerId\": \"$CUSTOMER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 1
      }
    ],
    \"shippingAddress\": \"Jl. Payment Test\"
  }")

ORDER_ID=$(echo "$ORDER_RESPONSE" | jq -r '.id')
```

***

## 13.3 Payment success

```bash
curl -k -i -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/payments" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: payment-success-001" \
  -d '{
    "provider": "MockPayment",
    "simulateResult": "Success"
  }'
```

Expected:

```json
{
  "paymentId": "...",
  "orderId": "...",
  "amount": 150000,
  "status": "Paid",
  "orderStatus": "Confirmed",
  "provider": "MockPayment",
  "paymentReference": "MOCK-...",
  "createdAt": "..."
}
```

***

## 13.4 Duplicate paid payment should fail

Run same payment success again:

```bash
curl -k -i -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/payments" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "provider": "MockPayment",
    "simulateResult": "Success"
  }'
```

Expected:

```http
422 Unprocessable Entity
```

or depending rule path:

```json
{
  "error": {
    "code": "PAYMENT_NOT_ALLOWED",
    "message": "Payment is only allowed when order status is Pending."
  }
}
```

Kalau order sudah `Confirmed`, NRules akan reject karena status bukan `Pending`. Kalau race duplicate paid terjadi sebelum status berubah, unique partial index tetap jadi backstop.

***

## 13.5 Payment failed

Create order baru, lalu:

```bash
curl -k -i -X POST "https://localhost:7000/api/v1/orders/$NEW_ORDER_ID/payments" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "provider": "MockPayment",
    "simulateResult": "Failed"
  }'
```

Expected:

```json
{
  "status": "Failed",
  "orderStatus": "Pending"
}
```

Order tetap `Pending`.

***

## 13.6 List payments

```bash
curl -k -i "https://localhost:7000/api/v1/orders/$ORDER_ID/payments" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN"
```

Expected:

```json
{
  "orderId": "...",
  "payments": [
    {
      "id": "...",
      "amount": 150000,
      "status": "Paid",
      "provider": "MockPayment",
      "paymentReference": "MOCK-...",
      "createdAt": "...",
      "updatedAt": "..."
    }
  ]
}
```

***

# 14. Concurrent Payment vs Cancel Manual Test

Create fresh order Pending, get `ORDER_ID` and `ORDER_VERSION`.

Terminal-style parallel:

```bash
curl -k -s -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/payments" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "provider": "MockPayment",
    "simulateResult": "Success"
  }' &

curl -k -s -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/cancel" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"expectedRowVersion\": $ORDER_VERSION,
    \"cancellationReason\": \"CustomerRequested\",
    \"reason\": \"Race test cancel.\"
  }" &

wait
```

Expected:

```text
Only consistent outcome:
- Payment wins then cancel may mark payment RefundRequired.
OR
- Cancel wins then payment rejected.
No Paid payment on Cancelled order without refund marker.
No inconsistent status.
```

***

# 15. Security & Production Notes

Batch 11 ini production-grade-ish karena:

```text
1. Payment endpoint protected JWT.
2. Customer hanya bisa pay own order.
3. Admin/Ops bisa process payment for operational flow.
4. Payment transaction locks order row FOR UPDATE.
5. Payment validation uses latest order status after lock.
6. NRules enforces only Pending order can be paid.
7. Duplicate paid payment prevented by:
   - order row lock
   - existing paid payment check
   - partial unique index uq_payments_one_paid_per_order
8. Payment success updates order and status history in same transaction.
9. Payment/cancel race is serialized by order row lock.
10. Failed payment does not mutate order state.
```

***

# 16. Commit Batch 11

```bash
git add .
git commit -m "feat: add payment flow with order row locking"
```

***

