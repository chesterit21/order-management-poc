Siap bro, lanjut **Batch 6: NRules Order Lifecycle**.

Target batch ini:

```text
1. IOrderRulesService
2. NRulesOrderRulesService
3. Order transition rules
4. Cancel eligibility rules
5. Payment allowed rules
6. Rule facts usage
7. Unit tests basic untuk rules
```

Setelah batch ini, business rules lifecycle order tidak hardcoded liar di service nanti. Nanti di Batch 8/10/11 kita tinggal panggil:

```csharp
_orderRulesService.ValidateOrderTransition(...)
_orderRulesService.ValidateCancel(...)
_orderRulesService.ValidatePayment(...)
```

***

# Batch 6 — NRules Order Lifecycle

## 1. Application Abstraction

### `src/OrderManagement.Application/Abstractions/Rules/IOrderRulesService.cs`

Replace isi file:

```csharp
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Domain.Rules.Results;

namespace OrderManagement.Application.Abstractions.Rules;

public interface IOrderRulesService
{
    RuleValidationResult ValidateOrderTransition(OrderTransitionFact fact);

    RuleValidationResult ValidateCancel(CancelOrderFact fact);

    RuleValidationResult ValidatePayment(PaymentFact fact);
}
```

***

# 2. Rule Result Enhancement

Kita tambahin helper untuk convert fact result dengan fallback.

### `src/OrderManagement.Domain/Rules/Results/RuleValidationResult.cs`

Replace isi file:

```csharp
namespace OrderManagement.Domain.Rules.Results;

public sealed class RuleValidationResult
{
    private RuleValidationResult(bool isAllowed, string? errorCode, string? errorMessage)
    {
        IsAllowed = isAllowed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsAllowed { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static RuleValidationResult Allowed()
    {
        return new RuleValidationResult(true, null, null);
    }

    public static RuleValidationResult Rejected(string errorCode, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Error code is required.", nameof(errorCode));
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message is required.", nameof(errorMessage));
        }

        return new RuleValidationResult(false, errorCode, errorMessage);
    }
}
```

> Kalau file lu sudah sama dari batch sebelumnya, tidak perlu ubah. Gue tulis ulang biar clear.

***

# 3. Infrastructure NRules Service

## `src/OrderManagement.Infrastructure/Rules/NRulesOrderRulesService.cs`

Replace isi file:

```csharp
using Microsoft.Extensions.Logging;
using NRules;
using NRules.Fluent;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Domain.Rules.Results;

namespace OrderManagement.Infrastructure.Rules;

public sealed class NRulesOrderRulesService : IOrderRulesService
{
    private static readonly Lazy<ISessionFactory> SessionFactory = new(CreateSessionFactory);

    private readonly ILogger<NRulesOrderRulesService> _logger;

    public NRulesOrderRulesService(ILogger<NRulesOrderRulesService> logger)
    {
        _logger = logger;
    }

    public RuleValidationResult ValidateOrderTransition(OrderTransitionFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var workingFact = new OrderTransitionFact
        {
            OrderId = fact.OrderId,
            CurrentStatus = fact.CurrentStatus,
            TargetStatus = fact.TargetStatus,
            RequestedByUserId = fact.RequestedByUserId,
            RequestedByRole = fact.RequestedByRole
        };

        FireRules(workingFact);

        if (workingFact.IsAllowed)
        {
            _logger.LogDebug(
                "Order transition allowed. OrderId={OrderId} CurrentStatus={CurrentStatus} TargetStatus={TargetStatus}",
                workingFact.OrderId,
                workingFact.CurrentStatus,
                workingFact.TargetStatus);

            return RuleValidationResult.Allowed();
        }

        var errorCode = string.IsNullOrWhiteSpace(workingFact.ErrorCode)
            ? ErrorCodes.InvalidOrderStatusTransition
            : workingFact.ErrorCode;

        var errorMessage = string.IsNullOrWhiteSpace(workingFact.ErrorMessage)
            ? $"Order status cannot be changed from {workingFact.CurrentStatus} to {workingFact.TargetStatus}."
            : workingFact.ErrorMessage;

        _logger.LogInformation(
            "Order transition rejected. OrderId={OrderId} CurrentStatus={CurrentStatus} TargetStatus={TargetStatus} ErrorCode={ErrorCode}",
            workingFact.OrderId,
            workingFact.CurrentStatus,
            workingFact.TargetStatus,
            errorCode);

        return RuleValidationResult.Rejected(errorCode, errorMessage);
    }

    public RuleValidationResult ValidateCancel(CancelOrderFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var workingFact = new CancelOrderFact
        {
            OrderId = fact.OrderId,
            CustomerId = fact.CustomerId,
            CurrentStatus = fact.CurrentStatus,
            RequestedByUserId = fact.RequestedByUserId,
            RequestedByRole = fact.RequestedByRole
        };

        FireRules(workingFact);

        if (workingFact.IsAllowed)
        {
            _logger.LogDebug(
                "Cancel order allowed. OrderId={OrderId} CurrentStatus={CurrentStatus}",
                workingFact.OrderId,
                workingFact.CurrentStatus);

            return RuleValidationResult.Allowed();
        }

        var errorCode = string.IsNullOrWhiteSpace(workingFact.ErrorCode)
            ? ErrorCodes.InvalidOrderStatusTransition
            : workingFact.ErrorCode;

        var errorMessage = string.IsNullOrWhiteSpace(workingFact.ErrorMessage)
            ? $"Order cannot be cancelled because current status is {workingFact.CurrentStatus}."
            : workingFact.ErrorMessage;

        _logger.LogInformation(
            "Cancel order rejected. OrderId={OrderId} CurrentStatus={CurrentStatus} ErrorCode={ErrorCode}",
            workingFact.OrderId,
            workingFact.CurrentStatus,
            errorCode);

        return RuleValidationResult.Rejected(errorCode, errorMessage);
    }

    public RuleValidationResult ValidatePayment(PaymentFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var workingFact = new PaymentFact
        {
            OrderId = fact.OrderId,
            CustomerId = fact.CustomerId,
            CurrentOrderStatus = fact.CurrentOrderStatus,
            RequestedByUserId = fact.RequestedByUserId,
            RequestedByRole = fact.RequestedByRole,
            HasExistingPaidPayment = fact.HasExistingPaidPayment
        };

        FireRules(workingFact);

        if (workingFact.IsAllowed)
        {
            _logger.LogDebug(
                "Payment allowed. OrderId={OrderId} CurrentOrderStatus={CurrentOrderStatus}",
                workingFact.OrderId,
                workingFact.CurrentOrderStatus);

            return RuleValidationResult.Allowed();
        }

        var errorCode = string.IsNullOrWhiteSpace(workingFact.ErrorCode)
            ? ErrorCodes.PaymentNotAllowed
            : workingFact.ErrorCode;

        var errorMessage = string.IsNullOrWhiteSpace(workingFact.ErrorMessage)
            ? "Payment is only allowed when order status is Pending."
            : workingFact.ErrorMessage;

        _logger.LogInformation(
            "Payment rejected. OrderId={OrderId} CurrentOrderStatus={CurrentOrderStatus} ErrorCode={ErrorCode}",
            workingFact.OrderId,
            workingFact.CurrentOrderStatus,
            errorCode);

        return RuleValidationResult.Rejected(errorCode, errorMessage);
    }

    private static void FireRules<TFact>(TFact fact)
        where TFact : class
    {
        var session = SessionFactory.Value.CreateSession();
        session.Insert(fact);
        session.Fire();
    }

    private static ISessionFactory CreateSessionFactory()
    {
        var repository = new RuleRepository();

        repository.Load(loadSpecification =>
        {
            loadSpecification.From(typeof(NRulesOrderRulesService).Assembly);
        });

        return repository.Compile();
    }
}
```

## Kenapa pakai `Lazy<ISessionFactory>`?

Karena compile rule set itu relatif mahal. Jadi:

```text
Rules compiled once per application lifetime.
Session dibuat per validation call.
Tidak share ISession antar request.
Lebih aman untuk concurrency.
```

***

# 4. Order Transition Rules

## 4.1 `PendingToConfirmedRule.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Rules/Rules/PendingToConfirmedRule.cs
```

```csharp
using NRules.Fluent.Dsl;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class PendingToConfirmedRule : Rule
{
    public override void Define()
    {
        OrderTransitionFact fact = null!;

        When()
            .Match(() => fact,
                x => x.CurrentStatus == OrderStatus.Pending,
                x => x.TargetStatus == OrderStatus.Confirmed);

        Then()
            .Do(_ => fact.IsAllowed = true);
    }
}
```

***

## 4.2 `PendingToCancelledRule.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Rules/Rules/PendingToCancelledRule.cs
```

```csharp
using NRules.Fluent.Dsl;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class PendingToCancelledRule : Rule
{
    public override void Define()
    {
        OrderTransitionFact fact = null!;

        When()
            .Match(() => fact,
                x => x.CurrentStatus == OrderStatus.Pending,
                x => x.TargetStatus == OrderStatus.Cancelled);

        Then()
            .Do(_ => fact.IsAllowed = true);
    }
}
```

***

## 4.3 `ConfirmedToShippedRule.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Rules/Rules/ConfirmedToShippedRule.cs
```

```csharp
using NRules.Fluent.Dsl;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class ConfirmedToShippedRule : Rule
{
    public override void Define()
    {
        OrderTransitionFact fact = null!;

        When()
            .Match(() => fact,
                x => x.CurrentStatus == OrderStatus.Confirmed,
                x => x.TargetStatus == OrderStatus.Shipped);

        Then()
            .Do(_ => fact.IsAllowed = true);
    }
}
```

***

## 4.4 `ConfirmedToCancelledRule.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Rules/Rules/ConfirmedToCancelledRule.cs
```

```csharp
using NRules.Fluent.Dsl;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class ConfirmedToCancelledRule : Rule
{
    public override void Define()
    {
        OrderTransitionFact fact = null!;

        When()
            .Match(() => fact,
                x => x.CurrentStatus == OrderStatus.Confirmed,
                x => x.TargetStatus == OrderStatus.Cancelled);

        Then()
            .Do(_ => fact.IsAllowed = true);
    }
}
```

***

## 4.5 `ShippedToDeliveredRule.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Rules/Rules/ShippedToDeliveredRule.cs
```

```csharp
using NRules.Fluent.Dsl;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class ShippedToDeliveredRule : Rule
{
    public override void Define()
    {
        OrderTransitionFact fact = null!;

        When()
            .Match(() => fact,
                x => x.CurrentStatus == OrderStatus.Shipped,
                x => x.TargetStatus == OrderStatus.Delivered);

        Then()
            .Do(_ => fact.IsAllowed = true);
    }
}
```

***

## 4.6 `TerminalOrderStateRule.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Rules/Rules/TerminalOrderStateRule.cs
```

```csharp
using NRules.Fluent.Dsl;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class TerminalOrderStateRule : Rule
{
    public override void Define()
    {
        OrderTransitionFact fact = null!;

        When()
            .Match(() => fact,
                x => x.CurrentStatus is OrderStatus.Delivered or OrderStatus.Cancelled);

        Then()
            .Do(_ =>
            {
                fact.IsAllowed = false;
                fact.ErrorCode = ErrorCodes.OrderTerminalState;
                fact.ErrorMessage = $"Order is already in terminal state {fact.CurrentStatus}.";
            });
    }
}
```

***

# 5. Cancel Eligibility Rule

## `CancelAllowedRule.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Rules/Rules/CancelAllowedRule.cs
```

```csharp
using NRules.Fluent.Dsl;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class CancelAllowedRule : Rule
{
    public override void Define()
    {
        CancelOrderFact fact = null!;

        When()
            .Match(() => fact);

        Then()
            .Do(_ =>
            {
                if (fact.CurrentStatus is OrderStatus.Pending or OrderStatus.Confirmed)
                {
                    fact.IsAllowed = true;
                    return;
                }

                fact.IsAllowed = false;
                fact.ErrorCode = fact.CurrentStatus == OrderStatus.Cancelled
                    ? ErrorCodes.OrderAlreadyCancelled
                    : ErrorCodes.InvalidOrderStatusTransition;

                fact.ErrorMessage = $"Order cannot be cancelled because current status is {fact.CurrentStatus}.";
            });
    }
}
```

> Nanti authorization owner/customer akan tetap dicek di application service. NRules di sini fokus ke business lifecycle status.

***

# 6. Payment Allowed Rule

## `PaymentAllowedRule.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Rules/Rules/PaymentAllowedRule.cs
```

```csharp
using NRules.Fluent.Dsl;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class PaymentAllowedRule : Rule
{
    public override void Define()
    {
        PaymentFact fact = null!;

        When()
            .Match(() => fact);

        Then()
            .Do(_ =>
            {
                if (fact.HasExistingPaidPayment)
                {
                    fact.IsAllowed = false;
                    fact.ErrorCode = ErrorCodes.PaymentAlreadyPaid;
                    fact.ErrorMessage = "This order already has a successful payment.";
                    return;
                }

                if (fact.CurrentOrderStatus == OrderStatus.Pending)
                {
                    fact.IsAllowed = true;
                    return;
                }

                fact.IsAllowed = false;
                fact.ErrorCode = ErrorCodes.PaymentNotAllowed;
                fact.ErrorMessage = "Payment is only allowed when order status is Pending.";
            });
    }
}
```

***

# 7. Infrastructure DI Update

Register `IOrderRulesService`.

## `src/OrderManagement.Infrastructure/DependencyInjection.cs`

Replace isi file:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Infrastructure.Database;
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

        services.AddSingleton<IOrderRulesService, NRulesOrderRulesService>();

        return services;
    }
}
```

> `NRulesOrderRulesService` dibuat singleton karena dia stateless dan `ISessionFactory` juga static lazy. Setiap call tetap create `ISession` baru.

***

# 8. Unit Tests Basic untuk Rules

Karena rules implementation ada di Infrastructure, test project unit perlu reference Infrastructure juga.

Run command:

```bash
dotnet add tests/OrderManagement.Tests/OrderManagement.Tests.csproj reference src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj
```

***

## 8.1 `OrderStatusTransitionTests.cs`

Replace:

```text
tests/OrderManagement.Tests/Domain/OrderStatusTransitionTests.cs
```

Isi:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Infrastructure.Rules;

namespace OrderManagement.Tests.Domain;

public sealed class OrderStatusTransitionTests
{
    private readonly NRulesOrderRulesService _rulesService = new(
        NullLogger<NRulesOrderRulesService>.Instance);

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    public void ValidateOrderTransition_ShouldAllow_ValidTransitions(
        OrderStatus currentStatus,
        OrderStatus targetStatus)
    {
        var fact = CreateTransitionFact(currentStatus, targetStatus);

        var result = _rulesService.ValidateOrderTransition(fact);

        result.IsAllowed.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    public void ValidateOrderTransition_ShouldReject_InvalidTransitions(
        OrderStatus currentStatus,
        OrderStatus targetStatus)
    {
        var fact = CreateTransitionFact(currentStatus, targetStatus);

        var result = _rulesService.ValidateOrderTransition(fact);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOrderStatusTransition);
        result.ErrorMessage.Should().Contain(currentStatus.ToString());
        result.ErrorMessage.Should().Contain(targetStatus.ToString());
    }

    [Theory]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    public void ValidateOrderTransition_ShouldReject_TerminalStates(
        OrderStatus currentStatus,
        OrderStatus targetStatus)
    {
        var fact = CreateTransitionFact(currentStatus, targetStatus);

        var result = _rulesService.ValidateOrderTransition(fact);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.OrderTerminalState);
        result.ErrorMessage.Should().Contain("terminal state");
    }

    private static OrderTransitionFact CreateTransitionFact(
        OrderStatus currentStatus,
        OrderStatus targetStatus)
    {
        return new OrderTransitionFact
        {
            OrderId = Guid.NewGuid(),
            CurrentStatus = currentStatus,
            TargetStatus = targetStatus,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = UserRole.Admin
        };
    }
}
```

***

## 8.2 `CancelOrderRuleTests.cs`

Replace:

```text
tests/OrderManagement.Tests/Domain/CancelOrderRuleTests.cs
```

Isi:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Infrastructure.Rules;

namespace OrderManagement.Tests.Domain;

public sealed class CancelOrderRuleTests
{
    private readonly NRulesOrderRulesService _rulesService = new(
        NullLogger<NRulesOrderRulesService>.Instance);

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    public void ValidateCancel_ShouldAllow_WhenStatusIsPendingOrConfirmed(
        OrderStatus currentStatus)
    {
        var fact = CreateCancelFact(currentStatus);

        var result = _rulesService.ValidateCancel(fact);

        result.IsAllowed.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    public void ValidateCancel_ShouldReject_WhenStatusCannotBeCancelled(
        OrderStatus currentStatus)
    {
        var fact = CreateCancelFact(currentStatus);

        var result = _rulesService.ValidateCancel(fact);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOrderStatusTransition);
        result.ErrorMessage.Should().Contain(currentStatus.ToString());
    }

    [Fact]
    public void ValidateCancel_ShouldReject_WhenOrderAlreadyCancelled()
    {
        var fact = CreateCancelFact(OrderStatus.Cancelled);

        var result = _rulesService.ValidateCancel(fact);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.OrderAlreadyCancelled);
        result.ErrorMessage.Should().Contain(OrderStatus.Cancelled.ToString());
    }

    private static CancelOrderFact CreateCancelFact(OrderStatus currentStatus)
    {
        var customerId = Guid.NewGuid();

        return new CancelOrderFact
        {
            OrderId = Guid.NewGuid(),
            CustomerId = customerId,
            CurrentStatus = currentStatus,
            RequestedByUserId = customerId,
            RequestedByRole = UserRole.Customer
        };
    }
}
```

***

## 8.3 `PaymentRuleTests.cs`

Replace:

```text
tests/OrderManagement.Tests/Domain/PaymentRuleTests.cs
```

Isi:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Infrastructure.Rules;

namespace OrderManagement.Tests.Domain;

public sealed class PaymentRuleTests
{
    private readonly NRulesOrderRulesService _rulesService = new(
        NullLogger<NRulesOrderRulesService>.Instance);

    [Fact]
    public void ValidatePayment_ShouldAllow_WhenOrderIsPendingAndNoPaidPaymentExists()
    {
        var fact = CreatePaymentFact(
            OrderStatus.Pending,
            hasExistingPaidPayment: false);

        var result = _rulesService.ValidatePayment(fact);

        result.IsAllowed.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void ValidatePayment_ShouldReject_WhenOrderIsNotPending(
        OrderStatus currentStatus)
    {
        var fact = CreatePaymentFact(
            currentStatus,
            hasExistingPaidPayment: false);

        var result = _rulesService.ValidatePayment(fact);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PaymentNotAllowed);
        result.ErrorMessage.Should().Contain("Pending");
    }

    [Fact]
    public void ValidatePayment_ShouldReject_WhenPaidPaymentAlreadyExists()
    {
        var fact = CreatePaymentFact(
            OrderStatus.Pending,
            hasExistingPaidPayment: true);

        var result = _rulesService.ValidatePayment(fact);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PaymentAlreadyPaid);
        result.ErrorMessage.Should().Contain("already has a successful payment");
    }

    private static PaymentFact CreatePaymentFact(
        OrderStatus currentStatus,
        bool hasExistingPaidPayment)
    {
        var customerId = Guid.NewGuid();

        return new PaymentFact
        {
            OrderId = Guid.NewGuid(),
            CustomerId = customerId,
            CurrentOrderStatus = currentStatus,
            RequestedByUserId = customerId,
            RequestedByRole = UserRole.Customer,
            HasExistingPaidPayment = hasExistingPaidPayment
        };
    }
}
```

***

# 9. Build & Test

Run:

```bash
dotnet build
```

Lalu:

```bash
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj
```

Atau semua test:

```bash
dotnet test
```

***

# 10. Potensi Compile Issue dan Fix

## Issue 1 — `NRules` namespace tidak ketemu

Pastikan package NRules sudah ada di Infrastructure:

```bash
dotnet add src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj package NRules
```

## Issue 2 — test tidak bisa akses Infrastructure

Pastikan reference sudah ditambah:

```bash
dotnet add tests/OrderManagement.Tests/OrderManagement.Tests.csproj reference src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj
```

## Issue 3 — `NullLogger` tidak ketemu

Pastikan package ini ada di test project:

```bash
dotnet add tests/OrderManagement.Tests/OrderManagement.Tests.csproj package Microsoft.Extensions.Logging.Abstractions
```

***

# 11. Production Notes untuk Presentasi

Dengan Batch 6 ini, kita sudah bisa jelaskan:

```text
1. Order lifecycle rules tidak tersebar di controller/service.
2. Rules dikompilasi sekali via ISessionFactory.
3. Setiap validation memakai session baru, aman untuk concurrent request.
4. Terminal state punya explicit rejection.
5. Cancel eligibility dan payment eligibility punya rule masing-masing.
6. Application service nanti hanya konsumsi IOrderRulesService.
7. Rules sudah punya unit tests.
```

Poin penting untuk presentasi:

> “Kami tidak mengandalkan UI atau controller untuk menjaga lifecycle order. Business transition dikunci di rules service dan tetap divalidasi setelah database row lock membaca status terbaru.”

***

# 12. Commit Batch 6

```bash
git add .
git commit -m "feat: add NRules order lifecycle validation"
```

***
