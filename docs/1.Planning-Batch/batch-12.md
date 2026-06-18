Siap bro, kita masuk **Batch 12: Tests**. Ini batch yang bikin klaim concurrency lu kuat pas presentasi.

Kita implement:

```text
Unit tests:
- payment validation
- payment NRules
- cancel reason behavior

Integration/concurrency tests:
- concurrent stock deduction
- idempotent create race
- concurrent status update
- payment vs cancel race
- duplicate payment prevention
```

> Catatan penting: integration tests ini pakai **Testcontainers PostgreSQL**, jadi butuh Docker engine running di machine Linux lu. Walaupun app runtime lu gak pakai docker-compose, khusus test integration pakai container isolated itu jauh lebih production-grade.

***

# Batch 12 — Tests

## 0. Pastikan Package

Kalau belum ada, run:

```bash
dotnet add tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj package Testcontainers.PostgreSql
dotnet add tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj package FluentAssertions
dotnet add tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj package Dapper
dotnet add tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj package Npgsql

dotnet add tests/OrderManagement.Tests/OrderManagement.Tests.csproj package FluentAssertions
dotnet add tests/OrderManagement.Tests/OrderManagement.Tests.csproj package Microsoft.Extensions.Logging.Abstractions
```

Pastikan integration test reference API:

```bash
dotnet add tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj reference src/OrderManagement.Api/OrderManagement.Api.csproj
dotnet add tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj reference src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj
```

***

# 1. Refactor Cancel Reason agar Testable

Kalau di Batch 10 V2 kita masih taruh cancel reason logic sebagai private static method di `OrderService`, lebih production-grade kalau dipisah jadi policy.

## 1.1 Create `IOrderCancellationPolicy.cs`

File:

```text
src/OrderManagement.Application/Abstractions/Orders/IOrderCancellationPolicy.cs
```

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Abstractions.Orders;

public interface IOrderCancellationPolicy
{
    OrderCancellationDecision Resolve(
        string? cancellationReason,
        string? freeTextReason,
        UserRole currentRole);
}

public sealed class OrderCancellationDecision
{
    public required OrderCancellationReason CancellationReason { get; init; }

    public required bool RestoreStock { get; init; }

    public required string ReasonText { get; init; }
}
```

***

## 1.2 Create `OrderCancellationPolicy.cs`

File:

```text
src/OrderManagement.Application/Services/OrderCancellationPolicy.cs
```

```csharp
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Constants;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class OrderCancellationPolicy : IOrderCancellationPolicy
{
    public OrderCancellationDecision Resolve(
        string? cancellationReason,
        string? freeTextReason,
        UserRole currentRole)
    {
        var resolvedReason = ResolveCancellationReason(cancellationReason, currentRole);
        var restoreStock = ShouldRestoreStock(resolvedReason);

        return new OrderCancellationDecision
        {
            CancellationReason = resolvedReason,
            RestoreStock = restoreStock,
            ReasonText = BuildReasonText(freeTextReason, resolvedReason, restoreStock)
        };
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

    private static string BuildReasonText(
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
}
```

***

## 1.3 Update `OrderService.cs`

Tambahkan field:

```csharp
private readonly IOrderCancellationPolicy _orderCancellationPolicy;
```

Update constructor parameter:

```csharp
IOrderCancellationPolicy orderCancellationPolicy,
```

Set field:

```csharp
_orderCancellationPolicy = orderCancellationPolicy;
```

Lalu di method `CancelAsync`, replace logic resolve reason dengan:

```csharp
var cancellationDecision = _orderCancellationPolicy.Resolve(
    command.CancellationReason,
    command.Reason,
    currentRole);

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
```

Hapus private methods lama:

```csharp
ResolveCancellationReason
ShouldRestoreStock
BuildCancellationReasonText
```

***

## 1.4 Update Application DI

File:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

Tambahkan:

```csharp
services.AddSingleton<IOrderCancellationPolicy, OrderCancellationPolicy>();
```

***

# 2. Unit Tests

## 2.1 Payment Validation Test

Create folder:

```bash
mkdir -p tests/OrderManagement.Tests/Application/Validators
```

File:

```text
tests/OrderManagement.Tests/Application/Validators/CreatePaymentCommandValidatorTests.cs
```

```csharp
using FluentAssertions;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.Validators.Payments;

namespace OrderManagement.Tests.Application.Validators;

public sealed class CreatePaymentCommandValidatorTests
{
    private readonly CreatePaymentCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldPass_WhenRequestIsValid()
    {
        var command = new CreatePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            Provider = "MockPayment",
            SimulateResult = "Success"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldFail_WhenOrderIdIsEmpty()
    {
        var command = new CreatePaymentCommand
        {
            OrderId = Guid.Empty,
            Provider = "MockPayment",
            SimulateResult = "Success"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreatePaymentCommand.OrderId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("Paid")]
    public void Validate_ShouldFail_WhenSimulateResultIsInvalid(string simulateResult)
    {
        var command = new CreatePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            Provider = "MockPayment",
            SimulateResult = simulateResult
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreatePaymentCommand.SimulateResult));
    }

    [Fact]
    public void Validate_ShouldFail_WhenProviderTooLong()
    {
        var command = new CreatePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            Provider = new string('A', 101),
            SimulateResult = "Success"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreatePaymentCommand.Provider));
    }
}
```

***

## 2.2 Payment NRules Test

Kalau file `PaymentRuleTests.cs` sudah ada dari Batch 6, pastikan isinya begini:

File:

```text
tests/OrderManagement.Tests/Domain/PaymentRuleTests.cs
```

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
        var fact = CreatePaymentFact(OrderStatus.Pending, hasExistingPaidPayment: false);

        var result = _rulesService.ValidatePayment(fact);

        result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void ValidatePayment_ShouldReject_WhenOrderIsNotPending(OrderStatus status)
    {
        var fact = CreatePaymentFact(status, hasExistingPaidPayment: false);

        var result = _rulesService.ValidatePayment(fact);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PaymentNotAllowed);
    }

    [Fact]
    public void ValidatePayment_ShouldReject_WhenPaidPaymentAlreadyExists()
    {
        var fact = CreatePaymentFact(OrderStatus.Pending, hasExistingPaidPayment: true);

        var result = _rulesService.ValidatePayment(fact);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PaymentAlreadyPaid);
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

## 2.3 Cancel Reason Behavior Test

File:

```text
tests/OrderManagement.Tests/Application/Services/OrderCancellationPolicyTests.cs
```

```csharp
using FluentAssertions;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Services;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Tests.Application.Services;

public sealed class OrderCancellationPolicyTests
{
    private readonly OrderCancellationPolicy _policy = new();

    [Fact]
    public void Resolve_ShouldDefaultCustomerToCustomerRequested_AndRestoreStock()
    {
        var result = _policy.Resolve(
            cancellationReason: null,
            freeTextReason: null,
            currentRole: UserRole.Customer);

        result.CancellationReason.Should().Be(OrderCancellationReason.CustomerRequested);
        result.RestoreStock.Should().BeTrue();
        result.ReasonText.Should().Contain("Stock restored");
    }

    [Fact]
    public void Resolve_ShouldDefaultAdminToOperationalIssue_AndRestoreStock()
    {
        var result = _policy.Resolve(
            cancellationReason: null,
            freeTextReason: "Admin cancel",
            currentRole: UserRole.Admin);

        result.CancellationReason.Should().Be(OrderCancellationReason.OperationalIssue);
        result.RestoreStock.Should().BeTrue();
        result.ReasonText.Should().Contain("Admin cancel");
    }

    [Theory]
    [InlineData("StockUnavailable")]
    [InlineData("InventoryMismatch")]
    public void Resolve_ShouldNotRestoreStock_ForPhysicalStockProblem(string reason)
    {
        var result = _policy.Resolve(
            cancellationReason: reason,
            freeTextReason: "Warehouse confirmed no stock",
            currentRole: UserRole.Admin);

        result.RestoreStock.Should().BeFalse();
        result.ReasonText.Should().Contain("Stock was not restored");
    }

    [Fact]
    public void Resolve_ShouldRejectCustomerUsingStockUnavailableReason()
    {
        var act = () => _policy.Resolve(
            cancellationReason: "StockUnavailable",
            freeTextReason: null,
            currentRole: UserRole.Customer);

        act.Should().Throw<ForbiddenAppException>();
    }

    [Fact]
    public void Resolve_ShouldRejectInvalidReason()
    {
        var act = () => _policy.Resolve(
            cancellationReason: "BadReason",
            freeTextReason: null,
            currentRole: UserRole.Admin);

        act.Should().Throw<BusinessRuleAppException>();
    }
}
```

***

# 3. Integration Test Infrastructure

## 3.1 Disable Parallelization

File:

```text
tests/OrderManagement.IntegrationTests/AssemblyInfo.cs
```

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

***

## 3.2 Test Factory

File:

```text
tests/OrderManagement.IntegrationTests/Infrastructure/OrderManagementApiFactory.cs
```

```csharp
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OrderManagement.IntegrationTests.Infrastructure;

public sealed class OrderManagementApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("order_management_tests")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    private HttpClient? _client;

    public string ConnectionString => _postgres.GetConnectionString();

    public HttpClient Client => _client ??= CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Force API startup so migration runner applies DB schema.
        _ = Client;

        await ResetDatabaseAsync();
    }

    public new async Task DisposeAsync()
    {
        _client?.Dispose();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
                ["Migration:Enabled"] = "true",
                ["Migration:Path"] = "db/migrations",
                ["Jwt:Issuer"] = "OrderManagement.Api.Tests",
                ["Jwt:Audience"] = "OrderManagement.Tests",
                ["Jwt:Secret"] = "TEST_ONLY_SECRET_MINIMUM_32_CHARS_1234567890",
                ["Jwt:AccessTokenExpirationMinutes"] = "60"
            };

            configBuilder.AddInMemoryCollection(overrides);
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(
            """
            TRUNCATE TABLE
                payments,
                idempotency_keys,
                order_status_history,
                inventory_movements,
                order_items,
                orders,
                products,
                users
            RESTART IDENTITY;
            """);

        var adminHash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);
        var opsHash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);
        var customer1Hash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);
        var customer2Hash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12);

        await connection.ExecuteAsync(
            """
            INSERT INTO users (id, username, password_hash, display_name, role, is_active)
            VALUES
                (@AdminId, 'admin', @AdminHash, 'System Admin', 'Admin', TRUE),
                (@OpsId, 'ops', @OpsHash, 'Operations User', 'Ops', TRUE),
                (@Customer1Id, 'customer1', @Customer1Hash, 'Customer One', 'Customer', TRUE),
                (@Customer2Id, 'customer2', @Customer2Hash, 'Customer Two', 'Customer', TRUE);
            """,
            new
            {
                AdminId = TestUsers.AdminId,
                OpsId = TestUsers.OpsId,
                Customer1Id = TestUsers.Customer1Id,
                Customer2Id = TestUsers.Customer2Id,
                AdminHash = adminHash,
                OpsHash = opsHash,
                Customer1Hash = customer1Hash,
                Customer2Hash = customer2Hash
            });

        await connection.ExecuteAsync(
            """
            INSERT INTO products (id, sku, name, stock_quantity, price, is_active)
            VALUES
                (@MouseId, 'PRD-MOUSE-001', 'Mouse Wireless', 15, 150000, TRUE),
                (@KeyboardId, 'PRD-KEYBOARD-001', 'Mechanical Keyboard', 20, 450000, TRUE),
                (@HeadsetId, 'PRD-HEADSET-001', 'Gaming Headset', 10, 350000, TRUE);
            """,
            new
            {
                MouseId = TestProducts.MouseId,
                KeyboardId = TestProducts.KeyboardId,
                HeadsetId = TestProducts.HeadsetId
            });
    }
}

public static class TestUsers
{
    public static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OpsId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Customer1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Customer2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
}

public static class TestProducts
{
    public static readonly Guid MouseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid KeyboardId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid HeadsetId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
}
```

***

## 3.3 Auth Helper

File:

```text
tests/OrderManagement.IntegrationTests/Helpers/AuthHelper.cs
```

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderManagement.IntegrationTests.Helpers;

public static class AuthHelper
{
    public static async Task<string> LoginAsync(
        HttpClient client,
        string username,
        string password = "Password123!")
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                username,
                password
            });

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        return document.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("Access token was missing.");
    }
}
```

***

## 3.4 HTTP Helper

File:

```text
tests/OrderManagement.IntegrationTests/Helpers/HttpJsonHelper.cs
```

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderManagement.IntegrationTests.Helpers;

public static class HttpJsonHelper
{
    public static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string url,
        string token,
        object body,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(json);
    }
}
```

***

## 3.5 Database Assert Helper

File:

```text
tests/OrderManagement.IntegrationTests/Helpers/DatabaseAssertHelper.cs
```

```csharp
using Dapper;
using Npgsql;

namespace OrderManagement.IntegrationTests.Helpers;

public static class DatabaseAssertHelper
{
    public static async Task<int> GetProductStockAsync(
        string connectionString,
        Guid productId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT stock_quantity
            FROM products
            WHERE id = @ProductId;
            """,
            new { ProductId = productId });
    }

    public static async Task<int> CountOrdersAsync(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM orders;");
    }

    public static async Task<int> CountPaidPaymentsAsync(
        string connectionString,
        Guid orderId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM payments
            WHERE order_id = @OrderId
              AND status = 'Paid';
            """,
            new { OrderId = orderId });
    }

    public static async Task<string> GetOrderStatusAsync(
        string connectionString,
        Guid orderId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<string>(
            """
            SELECT status
            FROM orders
            WHERE id = @OrderId;
            """,
            new { OrderId = orderId });
    }

    public static async Task<long> GetOrderRowVersionAsync(
        string connectionString,
        Guid orderId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<long>(
            """
            SELECT row_version
            FROM orders
            WHERE id = @OrderId;
            """,
            new { OrderId = orderId });
    }
}
```

***

## 3.6 Order API Helper

File:

```text
tests/OrderManagement.IntegrationTests/Helpers/OrderApiHelper.cs
```

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Helpers;

public static class OrderApiHelper
{
    public static async Task<Guid> CreateOrderAsync(
        HttpClient client,
        string token,
        Guid customerId,
        Guid productId,
        int quantity,
        string? idempotencyKey = null)
    {
        using var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Post,
            "/api/v1/orders",
            token,
            new
            {
                customerId,
                items = new[]
                {
                    new
                    {
                        productId,
                        quantity
                    }
                },
                shippingAddress = "Jl. Integration Test"
            },
            idempotencyKey ?? Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var json = await response.ReadJsonAsync();

        return json.RootElement.GetProperty("id").GetGuid();
    }

    public static async Task<HttpResponseMessage> CreateOrderRawAsync(
        HttpClient client,
        string token,
        Guid customerId,
        Guid productId,
        int quantity,
        string idempotencyKey)
    {
        var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Post,
            "/api/v1/orders",
            token,
            new
            {
                customerId,
                items = new[]
                {
                    new
                    {
                        productId,
                        quantity
                    }
                },
                shippingAddress = "Jl. Integration Test"
            },
            idempotencyKey);

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PayAsync(
        HttpClient client,
        string token,
        Guid orderId,
        string simulateResult = "Success")
    {
        using var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/orders/{orderId}/payments",
            token,
            new
            {
                provider = "MockPayment",
                simulateResult
            });

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> CancelAsync(
        HttpClient client,
        string token,
        Guid orderId,
        long expectedRowVersion,
        string cancellationReason = "CustomerRequested")
    {
        using var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/orders/{orderId}/cancel",
            token,
            new
            {
                expectedRowVersion,
                cancellationReason,
                reason = "Integration test cancel."
            });

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> UpdateStatusAsync(
        HttpClient client,
        string token,
        Guid orderId,
        long expectedRowVersion,
        string targetStatus)
    {
        using var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Patch,
            $"/api/v1/orders/{orderId}/status",
            token,
            new
            {
                targetStatus,
                expectedRowVersion,
                reason = "Integration test update."
            });

        return await client.SendAsync(request);
    }
}
```

***

# 4. Integration / Concurrency Tests

## 4.1 Concurrent Stock Deduction

File:

```text
tests/OrderManagement.IntegrationTests/Concurrency/ConcurrentStockDeductionTests.cs
```

```csharp
using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class ConcurrentStockDeductionTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public ConcurrentStockDeductionTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TwoConcurrentOrders_ShouldNotDeductStockMoreThanAvailable()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "customer1");

        var task1 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            10,
            Guid.NewGuid().ToString("N"));

        var task2 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            10,
            Guid.NewGuid().ToString("N"));

        var responses = await Task.WhenAll(task1, task2);

        responses.Count(x => x.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var finalStock = await DatabaseAssertHelper.GetProductStockAsync(
            _factory.ConnectionString,
            TestProducts.MouseId);

        finalStock.Should().Be(5);

        var orderCount = await DatabaseAssertHelper.CountOrdersAsync(_factory.ConnectionString);
        orderCount.Should().Be(1);
    }
}
```

***

## 4.2 Idempotent Create Race

File:

```text
tests/OrderManagement.IntegrationTests/Concurrency/IdempotentCreateRaceTests.cs
```

```csharp
using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class IdempotentCreateRaceTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public IdempotentCreateRaceTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SameIdempotencyKeyConcurrentCreate_ShouldCreateOnlyOneOrder()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "customer1");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var task1 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            10,
            idempotencyKey);

        var task2 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            10,
            idempotencyKey);

        var responses = await Task.WhenAll(task1, task2);

        responses.Should().Contain(x => x.StatusCode == HttpStatusCode.Created);

        responses.Should().OnlyContain(x =>
            x.StatusCode == HttpStatusCode.Created ||
            x.StatusCode == HttpStatusCode.Conflict);

        var orderCount = await DatabaseAssertHelper.CountOrdersAsync(_factory.ConnectionString);
        orderCount.Should().Be(1);

        var finalStock = await DatabaseAssertHelper.GetProductStockAsync(
            _factory.ConnectionString,
            TestProducts.MouseId);

        finalStock.Should().Be(5);
    }
}
```

***

## 4.3 Concurrent Status Update

File:

```text
tests/OrderManagement.IntegrationTests/Concurrency/ConcurrentStatusUpdateTests.cs
```

```csharp
using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class ConcurrentStatusUpdateTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public ConcurrentStatusUpdateTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentStatusUpdateAndCancel_ShouldAllowOnlyOneWinner()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var customerToken = await AuthHelper.LoginAsync(client, "customer1");
        var adminToken = await AuthHelper.LoginAsync(client, "admin");

        var orderId = await OrderApiHelper.CreateOrderAsync(
            client,
            customerToken,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1);

        var paymentResponse = await OrderApiHelper.PayAsync(
            client,
            customerToken,
            orderId);

        paymentResponse.EnsureSuccessStatusCode();

        var rowVersion = await DatabaseAssertHelper.GetOrderRowVersionAsync(
            _factory.ConnectionString,
            orderId);

        var shipTask = OrderApiHelper.UpdateStatusAsync(
            client,
            adminToken,
            orderId,
            rowVersion,
            "Shipped");

        var cancelTask = OrderApiHelper.CancelAsync(
            client,
            adminToken,
            orderId,
            rowVersion,
            "OperationalIssue");

        var responses = await Task.WhenAll(shipTask, cancelTask);

        responses.Count(x => x.IsSuccessStatusCode).Should().Be(1);
        responses.Count(x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity).Should().Be(1);

        var finalStatus = await DatabaseAssertHelper.GetOrderStatusAsync(
            _factory.ConnectionString,
            orderId);

        finalStatus.Should().BeOneOf("Shipped", "Cancelled");
    }
}
```

***

## 4.4 Payment vs Cancel Race

File:

```text
tests/OrderManagement.IntegrationTests/Concurrency/PaymentVsCancelRaceTests.cs
```

```csharp
using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class PaymentVsCancelRaceTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public PaymentVsCancelRaceTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PaymentAndCancelRace_ShouldKeepOrderConsistent()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var customerToken = await AuthHelper.LoginAsync(client, "customer1");

        var orderId = await OrderApiHelper.CreateOrderAsync(
            client,
            customerToken,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1);

        var rowVersion = await DatabaseAssertHelper.GetOrderRowVersionAsync(
            _factory.ConnectionString,
            orderId);

        var paymentTask = OrderApiHelper.PayAsync(
            client,
            customerToken,
            orderId,
            "Success");

        var cancelTask = OrderApiHelper.CancelAsync(
            client,
            customerToken,
            orderId,
            rowVersion,
            "CustomerRequested");

        var responses = await Task.WhenAll(paymentTask, cancelTask);

        responses.Count(x => x.IsSuccessStatusCode).Should().Be(1);
        responses.Count(x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity).Should().Be(1);

        var finalStatus = await DatabaseAssertHelper.GetOrderStatusAsync(
            _factory.ConnectionString,
            orderId);

        finalStatus.Should().BeOneOf("Confirmed", "Cancelled");

        var paidCount = await DatabaseAssertHelper.CountPaidPaymentsAsync(
            _factory.ConnectionString,
            orderId);

        if (finalStatus == "Cancelled")
        {
            paidCount.Should().Be(0);
        }
        else
        {
            paidCount.Should().Be(1);
        }
    }
}
```

***

## 4.5 Duplicate Payment Prevention

File:

```text
tests/OrderManagement.IntegrationTests/Concurrency/DuplicatePaymentPreventionTests.cs
```

```csharp
using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class DuplicatePaymentPreventionTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public DuplicatePaymentPreventionTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentSuccessfulPayments_ShouldCreateOnlyOnePaidPayment()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var customerToken = await AuthHelper.LoginAsync(client, "customer1");

        var orderId = await OrderApiHelper.CreateOrderAsync(
            client,
            customerToken,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1);

        var task1 = OrderApiHelper.PayAsync(
            client,
            customerToken,
            orderId,
            "Success");

        var task2 = OrderApiHelper.PayAsync(
            client,
            customerToken,
            orderId,
            "Success");

        var responses = await Task.WhenAll(task1, task2);

        responses.Count(x => x.IsSuccessStatusCode).Should().Be(1);
        responses.Count(x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity).Should().Be(1);

        var paidCount = await DatabaseAssertHelper.CountPaidPaymentsAsync(
            _factory.ConnectionString,
            orderId);

        paidCount.Should().Be(1);

        var finalStatus = await DatabaseAssertHelper.GetOrderStatusAsync(
            _factory.ConnectionString,
            orderId);

        finalStatus.Should().Be("Confirmed");
    }
}
```

***

# 5. Potential Compile Fixes

## 5.1 `Program` inaccessible

Pastikan di `Program.cs` API ada:

```csharp
public partial class Program;
```

Ini sudah kita tambahkan sebelumnya.

***

## 5.2 Testcontainers butuh Docker

Kalau error connection/container:

```bash
docker ps
```

Kalau Docker belum jalan:

```bash
sudo systemctl start docker
```

***

## 5.3 Migration checksum issue

Kalau lu pernah edit migration lama setelah applied di local dev DB, migration runner akan reject by checksum. Untuk integration test container fresh harus aman.

Untuk local dev, solusinya:

```text
Jangan edit migration lama yang sudah applied.
Buat migration baru.
```

***

# 6. Run Tests

Unit tests:

```bash
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj
```

Integration tests:

```bash
dotnet test tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj
```

Semua:

```bash
dotnet test
```

***

# 7. Apa yang Bisa Lu Klaim di Presentasi

Dengan Batch 12 ini, lu bisa bilang:

```text
1. Concurrent stock deduction diuji dengan dua request paralel.
2. Idempotency race diuji dengan dua request paralel dan key sama.
3. Concurrent status update diuji antara shipped vs cancel.
4. Payment vs cancel race diuji dan hasil akhir tetap konsisten.
5. Duplicate paid payment dicegah dengan order row lock + NRules + unique partial index.
6. Cancel reason behavior diuji, termasuk admin stock unavailable yang tidak restore stock.
```

Kalimat yang kuat:

> “Concurrency behavior bukan hanya dijelaskan secara teori, tapi dibuktikan lewat integration test dengan PostgreSQL asli menggunakan Testcontainers.”

***

# 8. Commit Batch 12

```bash
git add .
git commit -m "test: add unit and concurrency integration tests"
```

***
