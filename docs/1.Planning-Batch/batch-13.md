Siap bro. Kita masuk **Batch 13: Hardening & Cleanup**.

Fokus utama batch ini:

```text
1. Swagger JWT setup
2. Swagger Idempotency-Key header documentation
3. README final
4. Postman collection
5. Demo script
6. Fix order number generation pakai PostgreSQL sequence
7. Optional idempotency single transaction improvement — decision & production note
8. Test report notes
```

Untuk **order number generation**, kita benerin sekarang. Yang sebelumnya pakai:

```csharp
Random.Shared.NextInt64(...)
```

Itu kurang proper karena walaupun chance kecil, tetap bisa unique conflict. Kita pindahkan ke **PostgreSQL sequence** supaya deterministic, atomic, dan aman concurrency.

***

# Batch 13 — Hardening & Cleanup

***

# 1. Migration: PostgreSQL Sequence untuk Order Number

Create migration baru:

```text
db/migrations/012_create_order_number_sequence.sql
```

Isi:

```sql
CREATE SEQUENCE IF NOT EXISTS order_number_seq
    AS BIGINT
    START WITH 1
    INCREMENT BY 1
    MINVALUE 1
    NO MAXVALUE
    CACHE 10;
```

## Kenapa pakai sequence?

Karena sequence di PostgreSQL atomic dan aman untuk concurrent transaction. Format order number tetap readable:

```text
ORD-20260617-000001
ORD-20260617-000002
```

Sequence tidak reset harian. Itu intentional untuk POC production-grade sederhana:

```text
- Tidak perlu locking custom.
- Tidak perlu table counter manual.
- Tidak ada random collision.
- Unique constraint orders.order_number tetap menjadi safety net.
```

***

# 2. Update Create Order Persistence DTO

## `src/OrderManagement.Application/DTOs/Orders/CreateOrderPersistenceRequest.cs`

Replace:

```csharp
namespace OrderManagement.Application.DTOs.Orders;

public sealed class CreateOrderPersistenceRequest
{
    public required Guid OrderId { get; init; }

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

Kita hapus `OrderNumber` dari Application request karena repository/database yang generate.

***

# 3. Update OrderService — Remove Random Order Number

## `src/OrderManagement.Application/Services/OrderService.cs`

Di method `CreateAsync`, hapus:

```csharp
var now = _clock.UtcNow;

// POC order number. Production alternative: DB sequence.
var orderNumber = OrderNumber.Generate(now, Random.Shared.NextInt64(1, 999999)).Value;
```

Ganti jadi:

```csharp
var now = _clock.UtcNow;
```

Lalu bagian call repository, hapus property `OrderNumber`.

Sebelumnya:

```csharp
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
```

Ganti:

```csharp
var createResult = await _orderRepository.CreateAsync(
    new CreateOrderPersistenceRequest
    {
        OrderId = orderId,
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
```

Lalu hapus using yang sudah tidak dipakai:

```csharp
using OrderManagement.Domain.ValueObjects;
```

***

# 4. Update OrderRepository — Generate Order Number dari Sequence

Di file:

```text
src/OrderManagement.Infrastructure/Repositories/OrderRepository.cs
```

Tambahkan helper method ini di dalam class `OrderRepository`:

```csharp
private static async Task<string> GenerateOrderNumberAsync(
    System.Data.Common.DbConnection connection,
    System.Data.Common.DbTransaction transaction,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    const string sql = """
                       SELECT
                           'ORD-' ||
                           to_char(@Now::timestamptz, 'YYYYMMDD') ||
                           '-' ||
                           lpad(nextval('order_number_seq')::text, 6, '0');
                       """;

    return await connection.ExecuteScalarAsync<string>(
               new CommandDefinition(
                   sql,
                   new { Now = now },
                   transaction,
                   cancellationToken: cancellationToken))
           ?? throw new InvalidOperationException("Failed to generate order number.");
}
```

Lalu di method `CreateAsync`, setelah `SetLocalLockTimeoutAsync(...)`, tambahkan:

```csharp
var orderNumber = await GenerateOrderNumberAsync(
    connection,
    transaction,
    request.Now,
    cancellationToken);
```

Kemudian ubah insert order dari:

```csharp
request.OrderNumber,
```

menjadi:

```csharp
OrderNumber = orderNumber,
```

Contoh block insert final:

```csharp
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
            OrderNumber = orderNumber,
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
```

Lalu return result ubah:

```csharp
OrderNumber = request.OrderNumber,
```

menjadi:

```csharp
OrderNumber = orderNumber,
```

Dengan ini order number sudah concurrency-safe.

***

# 5. Swagger JWT Setup

## 5.1 Create Swagger Operation Filter untuk Headers

Create file:

```text
src/OrderManagement.Api/Extensions/SwaggerHeaderOperationFilter.cs
```

Isi:

```csharp
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderManagement.Api.Extensions;

public sealed class SwaggerHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= [];

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Correlation-ID",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Optional correlation ID for tracing request logs.",
            Schema = new OpenApiSchema
            {
                Type = "string"
            }
        });

        var relativePath = context.ApiDescription.RelativePath?.Trim('/').ToLowerInvariant();
        var httpMethod = context.ApiDescription.HttpMethod?.ToUpperInvariant();

        var isCreateOrderEndpoint =
            httpMethod == "POST" &&
            relativePath == "api/v1/orders";

        if (isCreateOrderEndpoint)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Required for create order. Use same key for retry of the same payload.",
                Schema = new OpenApiSchema
                {
                    Type = "string",
                    MaxLength = 200
                }
            });
        }
    }
}
```

***

## 5.2 Update `SwaggerExtensions.cs`

Replace:

```text
src/OrderManagement.Api/Extensions/SwaggerExtensions.cs
```

Dengan:

```csharp
using Microsoft.OpenApi.Models;

namespace OrderManagement.Api.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddApiSwagger(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var title = configuration["Swagger:Title"] ?? "Order Management API";
        var version = configuration["Swagger:Version"] ?? "v1";
        var description = configuration["Swagger:Description"] ??
                          "Prototype API with idempotency and concurrency-safe order management.";

        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(version, new OpenApiInfo
            {
                Title = title,
                Version = version,
                Description = description
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT Authorization header using Bearer scheme. Example: Bearer {token}",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    []
                }
            });

            options.OperationFilter<SwaggerHeaderOperationFilter>();
        });

        return services;
    }

    public static WebApplication UseApiSwagger(
        this WebApplication app)
    {
        var enabled = app.Configuration.GetValue<bool>("Swagger:Enabled");

        if (!enabled)
        {
            return app;
        }

        app.UseSwagger();

        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Management API v1");
            options.DisplayRequestDuration();
        });

        return app;
    }
}
```

***

## 5.3 Update `Program.cs`

Ganti:

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
```

Menjadi:

```csharp
builder.Services.AddApiSwagger(builder.Configuration);
```

Lalu ganti block:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

Menjadi:

```csharp
app.UseApiSwagger();
```

Swagger sekarang:

```text
- Ada Bearer JWT auth button.
- Ada X-Correlation-ID header di semua endpoint.
- Ada Idempotency-Key header required khusus POST /api/v1/orders.
```

***

# 6. Optional Idempotency Single Transaction Improvement

## Decision untuk POC ini

Untuk sekarang **tidak kita refactor ke single transaction** karena akan mengubah cukup besar arsitektur repository dan idempotency service.

Current design sudah aman untuk scenario requirement:

```text
- Unique constraint user_id + key + endpoint mencegah dua request key sama jadi owner.
- Request kedua akan dapat InProgress / stored response.
- Stock deduction tetap transaction-safe.
```

Tapi production enhancement yang bisa ditulis di README:

```text
Future improvement:
Move idempotency Begin, order creation, and MarkCompleted into the same database transaction using a shared UnitOfWork abstraction.
This would guarantee that idempotency record and resource creation commit atomically.
```

Kalau mau kita implement nanti, perlu batch tersendiri:

```text
Batch 14 optional:
- IUnitOfWork real implementation
- shared connection/transaction
- IdempotencyRepository overload with DbTransaction
- OrderRepository overload with DbTransaction
- single transaction orchestration in OrderService
```

Menurut gue untuk submission POC, current design + README tradeoff sudah defendable.

***

# 7. README Final

Replace:

```text
README.md
```

Dengan:

````md
# Order Management API POC

Prototype REST API for order management with idempotency, concurrency handling, PostgreSQL row locking, Dapper, NRules, JWT authentication, structured logging, and consistent error handling.

## Tech Stack

- ASP.NET Core API .NET 10
- PostgreSQL
- Dapper
- NRules
- JWT Bearer Authentication
- Serilog
- xUnit
- Testcontainers PostgreSQL

## Main Features

- Login with JWT
- Product list and detail
- Create order with stock deduction
- Idempotency-Key support for create order
- Get order detail
- List orders with filters and pagination
- Update order status
- Cancel order
- Conditional stock restore on cancel
- Mock payment flow
- Payment success confirms order
- Structured logging with correlation ID
- Consistent error response
- Database migration runner at startup
- Concurrency integration tests

## Order Lifecycle

Allowed transitions:

```text
Pending   -> Confirmed
Pending   -> Cancelled
Confirmed -> Shipped
Confirmed -> Cancelled
Shipped   -> Delivered
Delivered -> terminal
Cancelled -> terminal
````

Important:

```text
Cancelled status cannot be set through PATCH /status.
Use POST /cancel to ensure stock and audit trail are handled correctly.
```

## Cancellation Reasons

Supported cancellation reasons:

```text
CustomerRequested
StockUnavailable
InventoryMismatch
OperationalIssue
FraudSuspected
```

Stock restore behavior:

```text
CustomerRequested   -> restore stock
OperationalIssue    -> restore stock
FraudSuspected      -> restore stock
StockUnavailable    -> do not restore stock
InventoryMismatch   -> do not restore stock
```

This prevents system stock from being overstated when physical stock is unavailable due to offline/manual sales or warehouse mismatch.

## Idempotency Strategy

Create order requires:

```http
Idempotency-Key: {unique-key}
```

Behavior:

```text
New key + same user + same endpoint:
  process request

Same key + same payload + completed:
  return stored response

Same key + same payload + in progress:
  return 409 REQUEST_ALREADY_IN_PROGRESS

Same key + different payload:
  return 409 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD
```

Uniqueness:

```sql
UNIQUE (user_id, key, endpoint)
```

Request hash:

```text
SHA-256 of normalized JSON payload
```

## Concurrency Strategy

### Stock Deduction

When creating order:

```sql
SELECT ...
FROM products
WHERE id = ANY(@ProductIds)
ORDER BY id
FOR UPDATE;
```

Then stock is validated and deducted in the same transaction.

Why:

```text
- Prevents stock from going negative
- Ensures latest stock is checked
- ORDER BY id reduces deadlock risk for multi-product orders
```

### Status Update

When updating order status:

```sql
SELECT ...
FROM orders
WHERE id = @OrderId
FOR UPDATE;
```

Then:

```text
- row_version is checked
- NRules validates latest transition
- status_history is inserted
```

### Cancel Order

Cancel order locks:

```text
1. order row
2. product rows ordered by product_id
```

Then:

```text
- validates cancel eligibility
- restores or does not restore stock based on cancellation reason
- inserts inventory movement
- inserts status history
```

### Payment vs Cancel Race

Payment and cancel both lock the same order row.

Possible outcomes:

```text
Payment wins:
  Pending -> Confirmed
  Cancel may still happen from Confirmed
  Paid payment is marked RefundRequired

Cancel wins:
  Pending -> Cancelled
  Payment is rejected
```

## Race Conditions Covered

1. Concurrent stock deduction
2. Concurrent status update
3. Idempotent create race
4. Double cancel
5. Payment vs cancel
6. Duplicate payment
7. Manual/offline stock mismatch cancellation

## Error Response Format

```json
{
  "error": {
    "code": "ERROR_CODE",
    "message": "Human readable message.",
    "details": [],
    "correlationId": "trace-id",
    "timestamp": "2026-06-17T06:00:00Z"
  }
}
```

## Logging

Every request supports:

```http
X-Correlation-ID: optional-client-correlation-id
```

If missing, API generates one.

Logs include:

```text
CorrelationId
UserId
Username
Role
RequestPath
HttpMethod
StatusCode
ElapsedMs
```

## Database Migration

Migrations are stored in:

```text
db/migrations
```

At application startup, API applies pending migrations and tracks checksum in:

```text
schema_migrations
```

If an applied migration file is modified, startup fails. Create a new migration instead.

## Local Run

Set PostgreSQL connection string in:

```text
src/OrderManagement.Api/appsettings.Development.json
```

Run:

```bash
dotnet restore
dotnet build
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

Swagger:

```text
/swagger
```

Health:

```text
/health
```

## Seed Data

Run seed manually:

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -f db/seed/001_seed_users.sql
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -f db/seed/002_seed_products.sql
```

Default users:

```text
admin / Password123!
ops / Password123!
customer1 / Password123!
customer2 / Password123!
```

## Run Tests

Unit tests:

```bash
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj
```

Integration tests:

```bash
dotnet test tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj
```

All tests:

```bash
dotnet test
```

Integration tests use Testcontainers PostgreSQL and require Docker engine running.

## Known Limitations

* Payment provider is mocked.
* Inventory service is embedded in order API for prototype.
* No distributed message broker.
* No outbox pattern.
* No refresh token.
* Idempotency and order creation are currently separate transactions. Future improvement is a shared UnitOfWork transaction for Begin/Create/MarkCompleted.

````

---

# 8. Demo Script

Create file:

```text
docs/demo-script.md
````

Isi:

````md
# Demo Script

## 1. Login

```bash
ADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Password123!"}')

ADMIN_TOKEN=$(echo "$ADMIN_LOGIN" | jq -r '.accessToken')
````

```bash
CUSTOMER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}')

CUSTOMER_TOKEN=$(echo "$CUSTOMER_LOGIN" | jq -r '.accessToken')
CUSTOMER_ID=$(echo "$CUSTOMER_LOGIN" | jq -r '.user.id')
```

## 2. List Products

```bash
curl -k "https://localhost:7000/api/v1/products?page=1&pageSize=20" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN"
```

## 3. Create Order

```bash
PRODUCT_ID="<product-id>"
IDEMPOTENCY_KEY=$(uuidgen)

ORDER_RESPONSE=$(curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -H "X-Correlation-ID: demo-create-order-001" \
  -d "{
    \"customerId\": \"$CUSTOMER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 1
      }
    ],
    \"shippingAddress\": \"Jl. Demo No. 1\"
  }")

echo "$ORDER_RESPONSE" | jq
ORDER_ID=$(echo "$ORDER_RESPONSE" | jq -r '.id')
```

## 4. Retry Same Idempotency Key

```bash
curl -k -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{
    \"customerId\": \"$CUSTOMER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 1
      }
    ],
    \"shippingAddress\": \"Jl. Demo No. 1\"
  }" | jq
```

Expected:

```text
Same stored response. No duplicate order. No duplicate stock deduction.
```

## 5. Payment Success

```bash
curl -k -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/payments" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "provider": "MockPayment",
    "simulateResult": "Success"
  }' | jq
```

Expected:

```text
Payment Paid, Order Confirmed
```

## 6. Admin Update Status

Get row version:

```bash
ORDER_DETAIL=$(curl -k -s "https://localhost:7000/api/v1/orders/$ORDER_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN")

ROW_VERSION=$(echo "$ORDER_DETAIL" | jq -r '.rowVersion')
```

Ship:

```bash
curl -k -X PATCH "https://localhost:7000/api/v1/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"targetStatus\": \"Shipped\",
    \"expectedRowVersion\": $ROW_VERSION,
    \"reason\": \"Handed over to courier\"
  }" | jq
```

## 7. Cancel Because Stock Unavailable

For a Pending or Confirmed order:

```bash
curl -k -X POST "https://localhost:7000/api/v1/orders/$ORDER_ID/cancel" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"expectedRowVersion\": $ROW_VERSION,
    \"cancellationReason\": \"StockUnavailable\",
    \"reason\": \"Physical stock unavailable due to offline sale\"
  }" | jq
```

Expected:

```text
stockRestoreApplied = false
movement type = OrderCancelledNoRestore
```

## 8. Run Tests

```bash
dotnet test
```

````

---

# 9. Postman Collection

Create file:

```text
postman/OrderManagement.postman_collection.json
````

Isi minimal collection:

```json
{
  "info": {
    "name": "Order Management API",
    "_postman_id": "a8a1aa9d-4377-4e5c-9101-ordermanagement",
    "description": "Order Management POC API collection",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "variable": [
    {
      "key": "baseUrl",
      "value": "https://localhost:7000"
    },
    {
      "key": "token",
      "value": ""
    },
    {
      "key": "customerId",
      "value": ""
    },
    {
      "key": "productId",
      "value": ""
    },
    {
      "key": "orderId",
      "value": ""
    },
    {
      "key": "rowVersion",
      "value": "1"
    }
  ],
  "item": [
    {
      "name": "Auth - Login Customer",
      "request": {
        "method": "POST",
        "header": [
          {
            "key": "Content-Type",
            "value": "application/json"
          }
        ],
        "url": "{{baseUrl}}/api/v1/auth/login",
        "body": {
          "mode": "raw",
          "raw": "{\n  \"username\": \"customer1\",\n  \"password\": \"Password123!\"\n}"
        }
      }
    },
    {
      "name": "Products - List",
      "request": {
        "method": "GET",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{token}}"
          },
          {
            "key": "X-Correlation-ID",
            "value": "postman-products-list"
          }
        ],
        "url": "{{baseUrl}}/api/v1/products?page=1&pageSize=20"
      }
    },
    {
      "name": "Orders - Create",
      "request": {
        "method": "POST",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{token}}"
          },
          {
            "key": "Content-Type",
            "value": "application/json"
          },
          {
            "key": "Idempotency-Key",
            "value": "{{$guid}}"
          },
          {
            "key": "X-Correlation-ID",
            "value": "postman-create-order"
          }
        ],
        "url": "{{baseUrl}}/api/v1/orders",
        "body": {
          "mode": "raw",
          "raw": "{\n  \"customerId\": \"{{customerId}}\",\n  \"items\": [\n    {\n      \"productId\": \"{{productId}}\",\n      \"quantity\": 1\n    }\n  ],\n  \"shippingAddress\": \"Jl. Postman Demo\"\n}"
        }
      }
    },
    {
      "name": "Orders - Get Detail",
      "request": {
        "method": "GET",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{token}}"
          }
        ],
        "url": "{{baseUrl}}/api/v1/orders/{{orderId}}"
      }
    },
    {
      "name": "Orders - List",
      "request": {
        "method": "GET",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{token}}"
          }
        ],
        "url": "{{baseUrl}}/api/v1/orders?page=1&pageSize=20"
      }
    },
    {
      "name": "Payments - Create Success",
      "request": {
        "method": "POST",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{token}}"
          },
          {
            "key": "Content-Type",
            "value": "application/json"
          }
        ],
        "url": "{{baseUrl}}/api/v1/orders/{{orderId}}/payments",
        "body": {
          "mode": "raw",
          "raw": "{\n  \"provider\": \"MockPayment\",\n  \"simulateResult\": \"Success\"\n}"
        }
      }
    },
    {
      "name": "Orders - Update Status",
      "request": {
        "method": "PATCH",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{token}}"
          },
          {
            "key": "Content-Type",
            "value": "application/json"
          }
        ],
        "url": "{{baseUrl}}/api/v1/orders/{{orderId}}/status",
        "body": {
          "mode": "raw",
          "raw": "{\n  \"targetStatus\": \"Shipped\",\n  \"expectedRowVersion\": {{rowVersion}},\n  \"reason\": \"Postman update status\"\n}"
        }
      }
    },
    {
      "name": "Orders - Cancel Customer Requested",
      "request": {
        "method": "POST",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{token}}"
          },
          {
            "key": "Content-Type",
            "value": "application/json"
          }
        ],
        "url": "{{baseUrl}}/api/v1/orders/{{orderId}}/cancel",
        "body": {
          "mode": "raw",
          "raw": "{\n  \"expectedRowVersion\": {{rowVersion}},\n  \"cancellationReason\": \"CustomerRequested\",\n  \"reason\": \"Customer requested cancellation\"\n}"
        }
      }
    },
    {
      "name": "Orders - Cancel Stock Unavailable",
      "request": {
        "method": "POST",
        "header": [
          {
            "key": "Authorization",
            "value": "Bearer {{token}}"
          },
          {
            "key": "Content-Type",
            "value": "application/json"
          }
        ],
        "url": "{{baseUrl}}/api/v1/orders/{{orderId}}/cancel",
        "body": {
          "mode": "raw",
          "raw": "{\n  \"expectedRowVersion\": {{rowVersion}},\n  \"cancellationReason\": \"StockUnavailable\",\n  \"reason\": \"Physical stock unavailable due to offline sale\"\n}"
        }
      }
    }
  ]
}
```

***

# 10. Postman Environment

Create file:

```text
postman/OrderManagement.local.postman_environment.json
```

Isi:

```json
{
  "id": "f4f1f63a-3b8d-44f4-9000-local-env",
  "name": "Order Management Local",
  "values": [
    {
      "key": "baseUrl",
      "value": "https://localhost:7000",
      "enabled": true
    },
    {
      "key": "token",
      "value": "",
      "enabled": true
    },
    {
      "key": "customerId",
      "value": "",
      "enabled": true
    },
    {
      "key": "productId",
      "value": "",
      "enabled": true
    },
    {
      "key": "orderId",
      "value": "",
      "enabled": true
    },
    {
      "key": "rowVersion",
      "value": "1",
      "enabled": true
    }
  ],
  "_postman_variable_scope": "environment",
  "_postman_exported_using": "M365 Copilot"
}
```

***

# 11. Test Report Notes

Create file:

```text
docs/test-report.md
```

Isi:

````md
# Test Report Notes

## Unit Tests

Covered:

```text
Payment validation
Payment NRules
Order transition NRules
Cancel eligibility NRules
Cancel reason policy
Idempotency request hash
Idempotency service behavior
````

## Integration Tests

Covered with PostgreSQL Testcontainers:

```text
Concurrent stock deduction
Idempotent create race
Concurrent status update
Payment vs cancel race
Duplicate payment prevention
```

## Key Assertions

### Concurrent Stock Deduction

Setup:

```text
Product stock = 15
Request A quantity = 10
Request B quantity = 10
```

Expected:

```text
Only one order succeeds
One order fails with conflict
Final stock = 5
Stock never negative
```

### Idempotent Create Race

Setup:

```text
Two POST /orders
Same user
Same Idempotency-Key
Same payload
```

Expected:

```text
Only one order created
Only one stock deduction
Second request gets either stored response or request-in-progress conflict
```

### Concurrent Status Update

Setup:

```text
Order Confirmed
Admin A -> Shipped
Admin B -> Cancel
```

Expected:

```text
Only one operation succeeds
Final status consistent
Loser receives conflict or business rule error
```

### Payment vs Cancel Race

Expected:

```text
Payment wins:
  order confirmed
  cancel may later mark payment refund required

Cancel wins:
  order cancelled
  payment rejected
```

### Duplicate Payment Prevention

Expected:

```text
Only one Paid payment exists
Order status Confirmed
Second payment rejected
```

## Production Notes

The tests demonstrate that the API protects critical sections through PostgreSQL row-level locking, row version checks, unique constraints, and business rules.

````

---

# 12. Optional: Add `docs/hardening-notes.md`

Create:

```text
docs/hardening-notes.md
````

Isi:

````md
# Hardening Notes

## Order Number

Order number is generated using PostgreSQL sequence:

```text
ORD-yyyyMMdd-sequence
````

This avoids random collision risk and is safe under concurrent requests.

## Idempotency

Current implementation inserts idempotency record before order processing and stores completed response after successful order creation.

The design protects against duplicate order creation for the same key through:

```text
UNIQUE (user_id, key, endpoint)
```

Future enhancement:

```text
Use shared UnitOfWork transaction so idempotency Begin, order creation, and MarkCompleted commit atomically.
```

## Cancellation Stock Restore

Cancellation reason controls stock behavior.

```text
StockUnavailable and InventoryMismatch do not restore stock.
```

This protects against overstating stock when physical stock was already sold offline or warehouse stock is mismatched.

## Payment

Payment and cancel both lock order row with FOR UPDATE. This serializes race between payment success and cancellation.

## Security

Current POC uses JWT access token only. Refresh token, account lockout, and external identity provider integration are out of scope.

````

---

# 13. Run Migration & Build

Run:

```bash
dotnet build
````

Run API:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

Saat startup, migration runner akan apply:

```text
012_create_order_number_sequence.sql
```

Cek sequence:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "\ds"
```

***

# 14. Run Tests

```bash
dotnet test
```

Kalau integration tests butuh Docker:

```bash
sudo systemctl start docker
docker ps
```

***

# 15. Commit Batch 13

```bash
git add .
git commit -m "chore: harden swagger docs order number and project documentation"
```

***

# 16. Final Production-Grade Story

Setelah Batch 13, project punya story yang kuat:

```text
1. API bukan CRUD biasa.
2. Stock concurrency dijaga row lock.
3. Status concurrency dijaga row lock + row_version.
4. Idempotency mencegah double submit.
5. NRules menjaga lifecycle order/payment/cancel.
6. Cancel reason membedakan stock restore dan no-restore.
7. Payment/cancel race aman.
8. Order number pakai PostgreSQL sequence, bukan random.
9. Error response konsisten.
10. Logging punya correlation ID.
11. Swagger siap untuk demo JWT dan Idempotency-Key.
12. Integration tests membuktikan concurrency behavior.
```
