Siap bro. **Batch 15: Final Bug Sweep & Build Stabilization** ini penting banget karena dari Batch 1 sampai 14C kita sudah banyak nambah constructor, DI, middleware, repository, migration, docs, dan internal page. Batch ini fokus ke:

```text
- Stabilize compile
- Fix DI lifetime issue
- Fix middleware order
- Verify migration order
- Update README/Postman
- Run build/test
- Checklist final sebelum submission
```

Ada beberapa **bug potensial yang wajib kita sweep** bro, terutama:

```text
1. Middleware constructor tidak boleh inject scoped service.
2. RequestLoggingMiddleware harus melihat final response status setelah GlobalException.
3. Bounded Channel DropWrite sulit dideteksi drop-nya.
4. Activity log page butuh cara pakai Bearer token.
5. DI registration harus lengkap setelah constructor berubah.
```

***

# Batch 15 — Final Bug Sweep & Build Stabilization

***

# 1. Fix Middleware Order

## Problem

Di batch sebelumnya `UseApiMiddlewares()` kemungkinan urutannya:

```csharp
CorrelationIdMiddleware
GlobalExceptionHandlingMiddleware
RequestLoggingMiddleware
```

Kalau begitu, saat exception terjadi:

```text
RequestLoggingMiddleware finally bisa jalan sebelum GlobalExceptionHandlingMiddleware set response status.
```

Akibatnya activity log `RequestCompleted` bisa mencatat status code yang salah.

## Fix

Update file:

```text
src/OrderManagement.Api/Extensions/MiddlewareExtensions.cs
```

Replace dengan:

```csharp
using OrderManagement.Api.Middleware;

namespace OrderManagement.Api.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseApiMiddlewares(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();

        // Request logging wraps GlobalExceptionHandling so it sees the final status code
        // after global exception middleware converts exception into HTTP response.
        app.UseMiddleware<RequestLoggingMiddleware>();

        app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

        return app;
    }
}
```

Final pipeline concept:

```text
CorrelationId
  -> RequestLogging
      -> GlobalExceptionHandling
          -> Controllers
```

***

# 2. Fix Scoped Service Injection in Middleware

## Problem

`GlobalExceptionHandlingMiddleware` sebelumnya inject:

```csharp
IActivityLogWriter
```

di constructor. Tapi `IActivityLogWriter` registered sebagai scoped. Conventional middleware instance dibuat dari root provider, jadi ini bisa error:

```text
Cannot resolve scoped service from root provider
```

## Fix

Jangan inject `IActivityLogWriter` di constructor middleware. Ambil dari `HttpContext.RequestServices`.

Update file:

```text
src/OrderManagement.Api/Middleware/GlobalExceptionHandlingMiddleware.cs
```

Pastikan constructor hanya begini:

```csharp
private readonly RequestDelegate _next;
private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

public GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    _next = next;
    _logger = logger;
}
```

Lalu method activity log jadi:

```csharp
private static void TryWriteRequestFailedActivity(
    HttpContext context,
    int statusCode,
    string correlationId,
    string errorCode,
    Exception exception)
{
    var activityLogWriter = context.RequestServices.GetService<IActivityLogWriter>();

    activityLogWriter?.TryWrite(
        ActivityLogTypes.RequestFailed,
        statusCode: statusCode,
        errorCode: errorCode,
        metadata: new
        {
            correlationId,
            path = context.Request.Path.Value,
            method = context.Request.Method,
            exceptionType = exception.GetType().Name
        });
}
```

Dan panggilnya tetap:

```csharp
TryWriteRequestFailedActivity(
    context,
    statusCode,
    correlationId,
    errorResponse.Error.Code,
    exception);
```

Pastikan using ada:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

***

# 3. Fix Activity Log Queue Drop Detection

## Problem

Kalau `BoundedChannelFullMode.DropWrite`, `TryWrite` bisa sulit dipakai untuk mendeteksi drop secara akurat. Untuk stabilization, lebih aman pakai `FullMode.Wait`, lalu:

```text
TryWrite false kalau penuh.
WriteAsync wait kalau critical.
```

Update file:

```text
src/OrderManagement.Infrastructure/ActivityLogs/BoundedChannelActivityLogQueue.cs
```

Ganti constructor channel creation jadi:

```csharp
_channel = Channel.CreateBounded<ActivityLogMessage>(
    new BoundedChannelOptions(capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
        AllowSynchronousContinuations = false
    });
```

Behavior final:

```text
TryEnqueue:
  non-blocking; if full returns false and logs dropped count.

EnqueueAsync:
  waits for space; use only for critical events if needed.
```

***

# 4. Verify ActivityLogTypes Final

Update file:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogTypes.cs
```

Pastikan minimal isinya lengkap seperti ini:

```csharp
namespace OrderManagement.Application.DTOs.ActivityLogs;

public static class ActivityLogTypes
{
    public const string RequestStarted = "RequestStarted";
    public const string RequestCompleted = "RequestCompleted";
    public const string RequestFailed = "RequestFailed";

    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";

    public const string IdempotencyAccepted = "IdempotencyAccepted";
    public const string IdempotencyReplayReturned = "IdempotencyReplayReturned";
    public const string IdempotencyConflict = "IdempotencyConflict";

    public const string OrderCreateStarted = "OrderCreateStarted";
    public const string OrderCreated = "OrderCreated";

    public const string OrderStatusChangeStarted = "OrderStatusChangeStarted";
    public const string OrderStatusChanged = "OrderStatusChanged";
    public const string OrderStatusRejected = "OrderStatusRejected";

    public const string OrderCancelStarted = "OrderCancelStarted";
    public const string OrderCancelled = "OrderCancelled";

    public const string StockDeducted = "StockDeducted";
    public const string StockRestored = "StockRestored";
    public const string StockNotRestored = "StockNotRestored";
    public const string InsufficientStockDetected = "InsufficientStockDetected";

    public const string PaymentCreateStarted = "PaymentCreateStarted";
    public const string PaymentCreated = "PaymentCreated";
    public const string PaymentPaid = "PaymentPaid";
    public const string PaymentFailed = "PaymentFailed";
    public const string PaymentRejected = "PaymentRejected";
    public const string PaymentRefundRequired = "PaymentRefundRequired";

    public const string ConcurrencyConflict = "ConcurrencyConflict";
}
```

***

# 5. Verify DI Registrations

## 5.1 Application DI

File:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

Pastikan ada semua:

```csharp
services.AddScoped<IAuthService, AuthService>();
services.AddScoped<IProductService, ProductService>();
services.AddScoped<IOrderService, OrderService>();
services.AddScoped<IPaymentService, PaymentService>();
services.AddScoped<IActivityLogQueryService, ActivityLogQueryService>();

services.AddSingleton<IOrderCancellationPolicy, OrderCancellationPolicy>();

services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
services.AddScoped<IValidator<ProductListQueryDto>, ProductListQueryDtoValidator>();
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
services.AddScoped<IValidator<ListOrdersQueryDto>, ListOrdersQueryValidator>();
services.AddScoped<IValidator<UpdateOrderStatusCommand>, UpdateOrderStatusCommandValidator>();
services.AddScoped<IValidator<CancelOrderCommand>, CancelOrderCommandValidator>();
services.AddScoped<IValidator<CreatePaymentCommand>, CreatePaymentCommandValidator>();
services.AddScoped<IValidator<ActivityLogQueryDto>, ActivityLogQueryDtoValidator>();
```

## 5.2 Infrastructure DI

File:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Pastikan ada semua:

```csharp
services.Configure<ActivityLogOptions>(
    configuration.GetSection(ActivityLogOptions.SectionName));

services.AddSingleton<IActivityLogQueue, BoundedChannelActivityLogQueue>();
services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
services.AddScoped<IActivityLogQueryRepository, ActivityLogQueryRepository>();
services.AddScoped<IActivityLogContextAccessor, HttpActivityLogContextAccessor>();
services.AddScoped<IActivityLogWriter, ActivityLogWriter>();
services.AddHostedService<ActivityLogBackgroundWorker>();
```

Dan repository utama:

```csharp
services.AddScoped<IUserRepository, UserRepository>();
services.AddScoped<IProductRepository, ProductRepository>();
services.AddScoped<IOrderRepository, OrderRepository>();
services.AddScoped<IPaymentRepository, PaymentRepository>();
services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
```

***

# 6. Verify Constructor Compatibility

Cek constructor berikut sudah sinkron dengan DI.

## `AuthService`

Harus punya:

```csharp
IUserRepository
IPasswordHasher
IJwtTokenGenerator
IClock
IValidator<LoginCommand>
IActivityLogWriter
ILogger<AuthService>
```

## `OrderService`

Harus punya:

```csharp
IOrderRepository
ICurrentUserContext
IRequestHashService
IIdempotencyService
IClock
IValidator<CreateOrderCommand>
IValidator<ListOrdersQueryDto>
IValidator<UpdateOrderStatusCommand>
IValidator<CancelOrderCommand>
IOrderCancellationPolicy
IActivityLogWriter
ILogger<OrderService>
```

## `PaymentService`

Harus punya:

```csharp
IPaymentRepository
IOrderRepository
ICurrentUserContext
IClock
IValidator<CreatePaymentCommand>
IActivityLogWriter
ILogger<PaymentService>
```

## `OrderRepository`

Harus punya:

```csharp
IDbConnectionFactory
IOrderRulesService
IActivityLogWriter
```

## `PaymentRepository`

Harus punya:

```csharp
IDbConnectionFactory
IOrderRulesService
IActivityLogWriter
```

## `IdempotencyService`

Harus punya:

```csharp
IIdempotencyRepository
IClock
IOptions<IdempotencyOptions>
IActivityLogWriter
ILogger<IdempotencyService>
```

***

# 7. Verify Program.cs Middleware Order

File:

```text
src/OrderManagement.Api/Program.cs
```

Recommended final order:

```csharp
var app = builder.Build();

await app.ApplyDatabaseMigrationsAsync();

app.UseApiMiddlewares();

app.UseApiSwagger();

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        if (httpContext.Items.TryGetValue("CorrelationId", out var correlationId))
        {
            diagnosticContext.Set("CorrelationId", correlationId);
        }

        diagnosticContext.Set("RequestPath", httpContext.Request.Path.Value);
        diagnosticContext.Set("HttpMethod", httpContext.Request.Method);
    };
});

app.UseHttpsRedirection();

app.UseCors("ClientApps");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
```

Note:

```text
UseAuthentication wajib sebelum UseAuthorization.
UseApiMiddlewares harus sangat awal supaya correlation/error/logging wrap semua.
```

***

# 8. Verify Migrations Order

Pastikan folder:

```text
db/migrations
```

berisi urutan ini:

```text
001_create_extensions.sql
002_create_users.sql
003_create_products.sql
004_create_orders.sql
005_create_order_items.sql
006_create_inventory_movements.sql
007_create_order_status_history.sql
008_create_idempotency_keys.sql
009_create_payments.sql
010_create_indexes.sql
011_update_inventory_movement_types.sql
012_create_order_number_sequence.sql
013_create_activity_logs.sql
```

## Important

Jangan edit migration lama yang sudah applied. Kalau butuh perubahan, buat migration baru.

Cek migration applied:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT migration_name, applied_at FROM schema_migrations ORDER BY migration_name;"
```

***

# 9. Verify Activity Log Page Auth

## API endpoint harus protected

```text
GET /api/v1/internal/activity-logs
GET /api/v1/internal/activity-logs/{id}
```

Harus:

```csharp
[Authorize(Policy = AuthorizationPolicies.AdminOrOps)]
```

## HTML page

Untuk POC demo, HTML shell boleh:

```csharp
[AllowAnonymous]
```

Tapi data API tetap protected dengan token input.

Final expected:

```text
/internal/activity-logs bisa dibuka.
Tapi data tidak keluar tanpa Admin/Ops JWT.
Customer token dapat 403.
```

***

# 10. README Activity Logs Section

Tambahkan section ini ke `README.md`.

````md
## Activity Logs and Tracing

The API supports asynchronous business activity logging for operational tracing.

Activity logs are written through a bounded in-memory queue and persisted by a background worker.

This avoids slowing down API request latency while still providing searchable logs for operations.

### Activity Log Events

Examples:

```text
RequestCompleted
RequestFailed
LoginSucceeded
LoginFailed
IdempotencyAccepted
IdempotencyReplayReturned
OrderCreateStarted
OrderCreated
StockDeducted
InsufficientStockDetected
OrderStatusChanged
OrderCancelled
StockRestored
StockNotRestored
PaymentPaid
PaymentFailed
PaymentRejected
PaymentRefundRequired
````

### Internal Activity Logs API

Admin/Ops only:

```http
GET /api/v1/internal/activity-logs
GET /api/v1/internal/activity-logs/{id}
```

Supported filters:

```text
correlationId
orderId
orderNumber
activityType
actorUserId
fromDate
toDate
page
pageSize
```

Example:

```bash
curl -k "https://localhost:7000/api/v1/internal/activity-logs?correlationId=demo-create-order-001&page=1&pageSize=50" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

### Internal Activity Logs Page

Open:

```text
https://localhost:7000/internal/activity-logs
```

Paste Admin/Ops JWT token into the page and search by correlation ID, order ID, order number, or activity type.

### Security

The system does not log:

```text
password
password hash
JWT token
Authorization header
full login request body
connection string
```

### Performance

Activity logs use:

```text
bounded channel
background worker
batch insert
indexed query columns
```

This keeps request path overhead low.

````

---

# 11. Update Postman Collection — Internal Logs

Tambahkan item berikut ke `postman/OrderManagement.postman_collection.json`.

## Internal Logs — List

```json
{
  "name": "Internal - Activity Logs List",
  "request": {
    "method": "GET",
    "header": [
      {
        "key": "Authorization",
        "value": "Bearer {{token}}"
      }
    ],
    "url": "{{baseUrl}}/api/v1/internal/activity-logs?page=1&pageSize=50"
  }
}
````

## Internal Logs — Filter by Correlation ID

```json
{
  "name": "Internal - Activity Logs By Correlation ID",
  "request": {
    "method": "GET",
    "header": [
      {
        "key": "Authorization",
        "value": "Bearer {{token}}"
      }
    ],
    "url": "{{baseUrl}}/api/v1/internal/activity-logs?correlationId={{correlationId}}&page=1&pageSize=50"
  }
}
```

## Internal Logs — Detail

```json
{
  "name": "Internal - Activity Log Detail",
  "request": {
    "method": "GET",
    "header": [
      {
        "key": "Authorization",
        "value": "Bearer {{token}}"
      }
    ],
    "url": "{{baseUrl}}/api/v1/internal/activity-logs/{{activityLogId}}"
  }
}
```

Tambahkan environment variables:

```json
{
  "key": "correlationId",
  "value": "",
  "enabled": true
},
{
  "key": "activityLogId",
  "value": "",
  "enabled": true
}
```

***

# 12. Add Stabilization Script

Create file:

```text
scripts/verify.sh
```

Isi:

```bash
#!/usr/bin/env bash

set -euo pipefail

echo "=========================================="
echo "Order Management POC Verification"
echo "=========================================="

echo ""
echo "Dotnet info:"
dotnet --version

echo ""
echo "Restoring..."
dotnet restore

echo ""
echo "Building..."
dotnet build --no-restore

echo ""
echo "Running unit tests..."
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj --no-build

echo ""
echo "Running integration tests..."
dotnet test tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj --no-build

echo ""
echo "Checking packages..."
dotnet list package --outdated || true

echo ""
echo "Verification completed."
```

Run:

```bash
chmod +x scripts/verify.sh
./scripts/verify.sh
```

***

# 13. Build Commands

Run clean build:

```bash
dotnet clean
dotnet restore
dotnet build
```

If successful:

```bash
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj
```

Then integration:

```bash
dotnet test tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj
```

All:

```bash
dotnet test
```

***

# 14. Runtime Smoke Test

Start API:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

Login admin:

```bash
ADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: smoke-login-admin-001" \
  -d '{"username":"admin","password":"Password123!"}')

ADMIN_TOKEN=$(echo "$ADMIN_LOGIN" | jq -r '.accessToken')
```

Check logs:

```bash
curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?correlationId=smoke-login-admin-001&page=1&pageSize=20" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  | jq
```

Expected:

```text
LoginSucceeded
RequestCompleted
```

Open page:

```text
https://localhost:7000/internal/activity-logs
```

Paste admin token, search:

```text
smoke-login-admin-001
```

***

# 15. Security Smoke Test

Customer must not access internal logs:

```bash
CUSTOMER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}')

CUSTOMER_TOKEN=$(echo "$CUSTOMER_LOGIN" | jq -r '.accessToken')

curl -k -i "https://localhost:7000/api/v1/internal/activity-logs?page=1&pageSize=20" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN"
```

Expected:

```text
403 FORBIDDEN
```

Check sensitive data not logged:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT id, metadata FROM activity_logs WHERE metadata::text ILIKE '%Password123%' OR metadata::text ILIKE '%Bearer%' OR metadata::text ILIKE '%Authorization%';"
```

Expected:

```text
0 rows
```

***

# 16. Common Compile Errors & Fixes

## Error: cannot resolve scoped service in middleware

Cause:

```text
Middleware constructor injects scoped service.
```

Fix:

```text
Use context.RequestServices.GetService<T>() inside Invoke/handler.
```

Apply to:

```text
GlobalExceptionHandlingMiddleware
RequestLoggingMiddleware if needed
```

***

## Error: constructor parameter not registered

Check DI:

```text
IActivityLogWriter
IActivityLogContextAccessor
IActivityLogQueryService
IActivityLogQueryRepository
IOrderCancellationPolicy
```

***

## Error: ActivityLogTypes missing constant

Add missing constants:

```text
OrderStatusChangeStarted
OrderCancelStarted
PaymentCreateStarted
```

***

## Error: page fetch returns 401

Cause:

```text
No bearer token passed from page.
```

Fix:

```text
Use token input and send Authorization header in fetch.
```

***

## Error: migration checksum changed

Cause:

```text
Old migration file edited after applied.
```

Fix for local dev only:

```text
Drop/recreate local database
```

or create new migration instead of editing old one.

***

# 17. Final Acceptance Checklist

Before commit:

```text
[ ] dotnet clean passes
[ ] dotnet restore passes
[ ] dotnet build passes
[ ] unit tests pass
[ ] integration tests pass
[ ] app starts and migration applies
[ ] Swagger works
[ ] login works
[ ] create order works
[ ] idempotency replay works
[ ] payment works
[ ] cancel works
[ ] activity logs are inserted
[ ] internal logs API Admin/Ops only
[ ] customer blocked from internal logs
[ ] internal logs page can search by correlation ID
[ ] no sensitive data in activity_logs
[ ] README updated
[ ] Postman updated
```

***

# 18. Commit Batch 15

```bash
git add .
git commit -m "chore: stabilize build and finalize activity log tracing"
```

***

Siap bro. Kita masuk **Final Submission Prep**. Ini tahap bukan nambah fitur besar lagi, tapi **merapikan repo supaya siap dikirim ke HR dan siap dipresentasikan**.

Target final:

```text
- README polished dan enak dibaca reviewer
- Demo data reset jelas
- Screenshot/demo evidence optional
- Architecture diagram optional tapi powerful
- Git repo bersih
- Tag release v1.0-poc
- Final verification checklist
```

***

# Final Submission Prep

## 1. Final README Polish

README harus jadi **entry point utama reviewer**. Jangan terlalu panjang liar, tapi harus menjawab pertanyaan penting.

Struktur final README yang gue rekomendasikan:

```text
# Order Management API POC

## 1. Overview
## 2. Tech Stack
## 3. Key Features
## 4. Architecture
## 5. Database Schema
## 6. Idempotency Strategy
## 7. Concurrency Strategy
## 8. Cancellation & Stock Restore Policy
## 9. Payment Flow
## 10. Error Handling
## 11. Logging & Activity Trace
## 12. Testing Strategy
## 13. How to Run
## 14. Demo Data
## 15. Demo Script
## 16. Known Limitations
## 17. Future Improvements
```

Tambahkan bagian singkat seperti ini di README.

***

## Suggested README Intro

```md
# Order Management API POC

This project is a production-oriented prototype for an Order Management API built with ASP.NET Core, PostgreSQL, Dapper, NRules, JWT authentication, idempotency handling, concurrency-safe stock deduction, structured logging, and activity tracing.

The prototype focuses on solving common order management issues:

- Duplicate order due to user double-click or client retry.
- Negative stock caused by concurrent order requests.
- Inconsistent order status caused by multiple admins updating the same order.
- Missing operational traceability due to insufficient logging.
- Payment/cancel race conditions.
- Cancellation scenarios where stock should or should not be restored.
```

***

## Suggested README Key Features

```md
## Key Features

- JWT-based login.
- Product list and detail.
- Create order with Idempotency-Key.
- Safe stock deduction using PostgreSQL row-level locking.
- Get order detail with items and status history.
- List orders with filter and pagination.
- Update order status with row version concurrency check.
- Cancel order with conditional stock restore.
- Mock payment flow.
- Payment success confirms order.
- Payment/cancel race protection.
- Activity log queue with background worker.
- Internal activity log API and tracing page.
- Global exception handling with consistent error response.
- Integration tests for concurrency scenarios.
```

***

# 2. Demo Data Reset Instructions

Karena reviewer bisa run berkali-kali, harus ada instruksi reset data yang jelas.

Buat file:

```text
docs/demo-reset.md
```

Isi rekomendasi:

````md
# Demo Data Reset

## 1. Reset Local Database

If you want a clean local database, recreate the database or truncate tables.

Example:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "
TRUNCATE TABLE
    activity_logs,
    payments,
    idempotency_keys,
    order_status_history,
    inventory_movements,
    order_items,
    orders,
    products,
    users
RESTART IDENTITY CASCADE;
"
````

## 2. Re-run Seed Data

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -f db/seed/001_seed_users.sql

PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -f db/seed/002_seed_products.sql
```

## 3. Default Users

```text
admin / Password123!
ops / Password123!
customer1 / Password123!
customer2 / Password123!
```

## 4. Default Products

```text
PRD-MOUSE-001       stock 15
PRD-KEYBOARD-001    stock 20
PRD-HEADSET-001     stock 10
```

````

---

# 3. Final Demo Script

Pastikan `docs/demo-script.md` punya urutan presentasi yang smooth.

Urutan demo paling kuat:

```text
1. Login customer.
2. List products.
3. Create order with Idempotency-Key.
4. Retry same Idempotency-Key.
5. Show stock deducted once.
6. Simulate concurrent stock deduction.
7. Payment success confirms order.
8. Admin update status with rowVersion.
9. Try stale rowVersion update.
10. Cancel CustomerRequested and show stock restored.
11. Cancel StockUnavailable and show stock not restored.
12. Open activity log page and search by correlation ID.
13. Run dotnet test.
````

Tambahkan highlight statement:

```md
## Demo Highlight

The most important demo points are:

- Reusing the same Idempotency-Key does not create duplicate order.
- Concurrent stock deduction cannot make stock negative.
- Stale row version update returns conflict.
- Cancel reason controls whether stock is restored.
- Activity log page can trace request/business timeline by correlation ID.
```

***

# 4. Screenshots Optional

Bikin folder:

```text
docs/screenshots/
```

Saran screenshot:

```text
docs/screenshots/swagger-auth.png
docs/screenshots/create-order.png
docs/screenshots/idempotency-replay.png
docs/screenshots/concurrent-stock-test.png
docs/screenshots/activity-log-page.png
docs/screenshots/test-result.png
```

Yang paling valuable buat reviewer:

```text
1. Swagger page dengan JWT auth.
2. Create order response.
3. Activity log page filter by correlation ID.
4. dotnet test result.
```

Kalau gak mau commit image, minimal tulis di README:

```md
## Screenshots

Screenshots are optional and can be generated during demo.
Recommended screenshots:

- Swagger JWT authorization.
- Create order success.
- Activity log timeline.
- Test result.
```

***

# 5. Final Architecture Diagram Optional

Bikin file:

```text
docs/architecture-diagram.md
```

Isi pakai Mermaid biar GitHub bisa render.

````md
# Architecture Diagram

```mermaid
flowchart TD
    Client[Client Apps<br/>MVC / Angular / React / Svelte / Vue] --> API[ASP.NET Core API]

    API --> Auth[JWT Auth Middleware]
    API --> Corr[Correlation ID Middleware]
    API --> Ex[Global Exception Middleware]
    API --> Controllers[Controllers]

    Controllers --> App[Application Services]
    App --> Rules[NRules Business Rules]
    App --> Idem[Idempotency Service]
    App --> Activity[Activity Log Queue]

    Activity --> Worker[Background Worker]
    Worker --> ActivityDb[(activity_logs)]

    App --> Repos[Dapper Repositories]
    Repos --> Pg[(PostgreSQL)]

    Pg --> Users[users]
    Pg --> Products[products]
    Pg --> Orders[orders]
    Pg --> Items[order_items]
    Pg --> Inv[inventory_movements]
    Pg --> Hist[order_status_history]
    Pg --> Pay[payments]
    Pg --> IdemTable[idempotency_keys]
````

````

Tambahkan concurrency diagram:

```md
## Create Order Concurrency

```mermaid
sequenceDiagram
    participant C as Client
    participant API as API
    participant IDEM as Idempotency
    participant DB as PostgreSQL

    C->>API: POST /orders + Idempotency-Key
    API->>IDEM: Begin key
    IDEM->>DB: INSERT idempotency InProgress
    DB-->>IDEM: unique key success
    API->>DB: BEGIN TRANSACTION
    API->>DB: SELECT products FOR UPDATE ORDER BY id
    API->>DB: Validate stock and deduct
    API->>DB: Insert order/items/movement/history
    API->>DB: COMMIT
    API->>IDEM: Mark Completed
    API-->>C: 201 Created
````

````

---

# 6. GitHub Repo Cleanup

Sebelum submission, cek struktur final:

```text
order-management-poc/
  src/
  tests/
  db/
  docs/
  postman/
  scripts/
  README.md
  .gitignore
  Directory.Build.props
  OrderManagement.sln
````

Pastikan tidak ada:

```text
bin/
obj/
.vs/
.idea/
*.user
.env
.env.local
TestResults/
coverage/
```

Command check:

```bash
git status
```

Cek file besar:

```bash
find . -type f -size +10M
```

Cek secret kasar:

```bash
grep -RniE "password=|secret|authorization|bearer|connectionstring" . \
  --exclude-dir=.git \
  --exclude-dir=bin \
  --exclude-dir=obj
```

Catatan:

```text
Development dummy JWT secret boleh ada, tapi README harus jelas local-only.
Production secret tidak boleh commit.
```

***

# 7. Final Verification Script

Pastikan `scripts/verify.sh` ada.

Run:

```bash
chmod +x scripts/verify.sh
./scripts/verify.sh
```

Kalau mau manual:

```bash
dotnet clean
dotnet restore
dotnet build
dotnet test
```

Kalau integration tests pakai Docker:

```bash
sudo systemctl start docker
docker ps
dotnet test tests/OrderManagement.IntegrationTests/OrderManagement.IntegrationTests.csproj
```

***

# 8. Final Runtime Smoke Test

Run API:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

Cek health:

```bash
curl -k -i https://localhost:7000/health
```

Login:

```bash
ADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: final-smoke-login" \
  -d '{"username":"admin","password":"Password123!"}')

ADMIN_TOKEN=$(echo "$ADMIN_LOGIN" | jq -r '.accessToken')
```

Cek internal logs:

```bash
curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?correlationId=final-smoke-login&page=1&pageSize=20" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  | jq
```

Open activity log page:

```text
https://localhost:7000/internal/activity-logs
```

Paste admin token, search:

```text
final-smoke-login
```

***

# 9. Final Commit

Kalau semua sudah oke:

```bash
git add .
git status
git commit -m "docs: prepare final submission materials"
```

Kalau ada stabilization fixes juga:

```bash
git commit -m "chore: final build stabilization before submission"
```

***

# 10. Tag Release

Buat annotated tag:

```bash
git tag -a v1.0-poc -m "Order Management API POC v1.0"
```

Push branch dan tag:

```bash
git push origin main
git push origin v1.0-poc
```

Kalau branch lu bukan `main`, cek:

```bash
git branch --show-current
```

Push current branch:

```bash
git push origin $(git branch --show-current)
```

***

# 11. GitHub Release Optional

Kalau mau lebih proper, bikin GitHub Release:

```text
Tag: v1.0-poc
Title: Order Management API POC v1.0
Description:
- Idempotent order creation
- Concurrency-safe stock deduction
- Order status row locking
- Cancel flow with conditional stock restore
- Mock payment flow
- Activity log tracing page
- Integration tests with PostgreSQL Testcontainers
```

***

# 12. Submission Message ke HR

Singkat tapi profesional:

```text
Halo Kak, berikut saya kirimkan repository untuk prototype Order Management API:

Repository:
{link-github}

Tag release:
v1.0-poc

Catatan:
- Tech stack: ASP.NET Core .NET 10, PostgreSQL, Dapper, NRules.
- Sudah mencakup idempotency key, concurrency-safe stock deduction, row locking, status transition validation, simple payment flow, structured logging, activity trace page, dan integration tests untuk skenario concurrency.
- Dokumentasi tersedia di README dan folder docs.

Terima kasih.
```

***

# 13. Final Submission Checklist

Sebelum kirim:

```text
[ ] README final sudah rapi.
[ ] docs/ lengkap.
[ ] Postman collection tersedia.
[ ] scripts/verify.sh tersedia.
[ ] dotnet build sukses.
[ ] dotnet test sukses.
[ ] API bisa run.
[ ] Migration apply sukses.
[ ] Seed instruction jelas.
[ ] Swagger JWT bisa dipakai.
[ ] Activity log page bisa dipakai.
[ ] Tidak ada secret production.
[ ] Git status clean.
[ ] Tag v1.0-poc dibuat.
[ ] Repo link bisa diakses public/private sesuai instruksi HR.
```
***