Siap bro. Kita lanjut **Batch 14B: Emit Activity Logs in Business Flow**.

Batch ini akan mengaktifkan fondasi queue dari Batch 14A supaya activity log beneran masuk dari flow penting:

```text
- request completed/failed activity logs from middleware
- login success/failed activity logs
- idempotency accepted/replay/conflict logs
- order created/stock deducted/insufficient stock logs
- status changed/cancelled/stock restored/no restore logs
- payment paid/failed/rejected logs
```

Prinsip production-grade yang kita jaga:

```text
- Tidak log password.
- Tidak log JWT/token.
- Tidak log Authorization header.
- Tidak log full request body.
- Activity logging pakai queue non-blocking.
- Business metadata secukupnya.
- Error code dan correlation id selalu masuk.
- Logging failure tidak boleh menggagalkan business process.
```

***

# Batch 14B — Emit Activity Logs in Business Flow

***

# 1. Update RequestLoggingMiddleware

File:

```text
src/OrderManagement.Api/Middleware/RequestLoggingMiddleware.cs
```

Replace full file:

```csharp
using System.Diagnostics;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
using Serilog.Context;

namespace OrderManagement.Api.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        var correlationId = GetCorrelationId(context);
        var userId = context.User.FindFirst("sub")?.Value;
        var username = context.User.FindFirst("username")?.Value;
        var role = context.User.FindFirst("role")?.Value;

        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("Username", username))
        using (LogContext.PushProperty("Role", role))
        using (LogContext.PushProperty("RequestPath", context.Request.Path.Value))
        using (LogContext.PushProperty("HttpMethod", context.Request.Method))
        {
            _logger.LogInformation(
                "Request started. Method={Method} Path={Path} CorrelationId={CorrelationId}",
                context.Request.Method,
                context.Request.Path.Value,
                correlationId);

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                _logger.LogInformation(
                    "Request completed. Method={Method} Path={Path} StatusCode={StatusCode} ElapsedMs={ElapsedMs} CorrelationId={CorrelationId}",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    correlationId);

                TryWriteRequestCompletedActivity(context, stopwatch.ElapsedMilliseconds);
            }
        }
    }

    private static void TryWriteRequestCompletedActivity(
        HttpContext context,
        long elapsedMs)
    {
        // Avoid logging internal log page/API too aggressively.
        if (context.Request.Path.StartsWithSegments("/api/v1/internal/activity-logs") ||
            context.Request.Path.StartsWithSegments("/internal/activity-logs"))
        {
            return;
        }

        var writer = context.RequestServices.GetService<IActivityLogWriter>();

        writer?.TryWrite(
            ActivityLogTypes.RequestCompleted,
            statusCode: context.Response.StatusCode,
            elapsedMs: elapsedMs,
            metadata: new
            {
                path = context.Request.Path.Value,
                method = context.Request.Method,
                queryString = context.Request.QueryString.HasValue
                    ? context.Request.QueryString.Value
                    : null
            });
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(CorrelationIdConstants.HttpContextItemName, out var value) &&
            value is string correlationId &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }

        return context.Request.Headers.TryGetValue(CorrelationIdConstants.HeaderName, out var values)
            ? values.FirstOrDefault() ?? string.Empty
            : string.Empty;
    }
}
```

Catatan:

```text
RequestCompleted akan tetap mencatat status code 2xx, 4xx, 5xx.
RequestFailed detail error code akan dicatat dari GlobalExceptionHandlingMiddleware.
```

***

# 2. Update GlobalExceptionHandlingMiddleware

File:

```text
src/OrderManagement.Api/Middleware/GlobalExceptionHandlingMiddleware.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

Update constructor menjadi:

```csharp
private readonly RequestDelegate _next;
private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
private readonly IActivityLogWriter _activityLogWriter;

public GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger,
    IActivityLogWriter activityLogWriter)
{
    _next = next;
    _logger = logger;
    _activityLogWriter = activityLogWriter;
}
```

Di method `HandleExceptionAsync`, setelah `LogException(...)`, tambahkan:

```csharp
TryWriteRequestFailedActivity(
    context,
    statusCode,
    correlationId,
    errorResponse.Error.Code,
    exception);
```

Tambahkan private method:

```csharp
private void TryWriteRequestFailedActivity(
    HttpContext context,
    int statusCode,
    string correlationId,
    string errorCode,
    Exception exception)
{
    _activityLogWriter.TryWrite(
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

Security note:

```text
Kita hanya log exception type, bukan stack trace di activity log.
Stack trace tetap ada di technical ILogger untuk server-side.
```

***

# 3. Update AuthService — Login Success/Failed Activity

File:

```text
src/OrderManagement.Application/Services/AuthService.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

Tambahkan field:

```csharp
private readonly IActivityLogWriter _activityLogWriter;
```

Update constructor parameter:

```csharp
IActivityLogWriter activityLogWriter,
```

Set field:

```csharp
_activityLogWriter = activityLogWriter;
```

Di invalid credentials block, sebelum throw:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.LoginFailed,
    errorCode: ErrorCodes.InvalidCredentials,
    metadata: new
    {
        username = normalizedUsername,
        reason = "InvalidCredentials"
    });
```

Di inactive user block, sebelum throw:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.LoginFailed,
    errorCode: ErrorCodes.InvalidCredentials,
    metadata: new
    {
        username = normalizedUsername,
        reason = "InactiveUser"
    });
```

Setelah login success log information, tambahkan:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.LoginSucceeded,
    metadata: new
    {
        userId = user.Id,
        username = user.Username,
        role = user.Role.ToString()
    });
```

## Security note

Kita tidak log password, password hash, atau JWT.

***

# 4. Update IdempotencyService — Accepted/Replay/Conflict

File:

```text
src/OrderManagement.Infrastructure/Idempotency/IdempotencyService.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

Tambahkan field:

```csharp
private readonly IActivityLogWriter _activityLogWriter;
```

Update constructor parameter:

```csharp
IActivityLogWriter activityLogWriter,
```

Set field:

```csharp
_activityLogWriter = activityLogWriter;
```

## 4.1 Saat idempotency accepted

Setelah log:

```csharp
_logger.LogInformation(
    "Idempotency key accepted for processing..."
);
```

Tambahkan:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.IdempotencyAccepted,
    metadata: new
    {
        endpoint,
        idempotencyKeyPrefix = MaskKey(key),
        recordId = insertedRecord.Id
    });
```

## 4.2 Saat different payload conflict

Sebelum throw:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.IdempotencyConflict,
    errorCode: "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD",
    metadata: new
    {
        endpoint,
        idempotencyKeyPrefix = MaskKey(key),
        reason = "DifferentPayload"
    });
```

## 4.3 Saat completed replay

Sebelum return stored response:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.IdempotencyReplayReturned,
    statusCode: existing.ResponseStatusCode,
    metadata: new
    {
        endpoint,
        idempotencyKeyPrefix = MaskKey(key),
        recordId = existing.Id,
        resourceType = existing.ResourceType,
        resourceId = existing.ResourceId
    });
```

## 4.4 Saat in progress conflict

Sebelum throw:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.IdempotencyConflict,
    errorCode: "REQUEST_ALREADY_IN_PROGRESS",
    metadata: new
    {
        endpoint,
        idempotencyKeyPrefix = MaskKey(key),
        recordId = existing.Id,
        reason = "InProgress"
    });
```

***

# 5. Update OrderService — OrderCreateStarted

File:

```text
src/OrderManagement.Application/Services/OrderService.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

Tambahkan field:

```csharp
private readonly IActivityLogWriter _activityLogWriter;
```

Update constructor parameter:

```csharp
IActivityLogWriter activityLogWriter,
```

Set field:

```csharp
_activityLogWriter = activityLogWriter;
```

Di method `CreateAsync`, setelah authorization check dan sebelum hash/idempotency:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.OrderCreateStarted,
    metadata: new
    {
        customerId = command.CustomerId,
        itemCount = command.Items.Count,
        totalQuantity = command.Items.Sum(x => x.Quantity)
    });
```

Di method `UpdateStatusAsync`, sebelum call repository:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.OrderStatusChanged,
    orderId: command.OrderId,
    beforeState: null,
    afterState: new
    {
        targetStatus = targetStatus.ToString(),
        expectedRowVersion = command.ExpectedRowVersion
    },
    metadata: new
    {
        requestedBy = currentUserId,
        requestedByRole = currentRole.ToString()
    });
```

> Note: Ini adalah “attempt log”. Log final status juga akan dicatat repository setelah commit.

Di method `CancelAsync`, sebelum repository call:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.OrderCancelled,
    orderId: command.OrderId,
    metadata: new
    {
        cancellationReason = cancellationDecision.CancellationReason.ToString(),
        restoreStock = cancellationDecision.RestoreStock,
        requestedBy = currentUserId,
        requestedByRole = currentRole.ToString()
    });
```

***

# 6. Update OrderRepository — Business Event Final Logs

File:

```text
src/OrderManagement.Infrastructure/Repositories/OrderRepository.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

Update constructor:

```csharp
private readonly IDbConnectionFactory _connectionFactory;
private readonly IOrderRulesService _orderRulesService;
private readonly IActivityLogWriter _activityLogWriter;

public OrderRepository(
    IDbConnectionFactory connectionFactory,
    IOrderRulesService orderRulesService,
    IActivityLogWriter activityLogWriter)
{
    _connectionFactory = connectionFactory;
    _orderRulesService = orderRulesService;
    _activityLogWriter = activityLogWriter;
}
```

***

## 6.1 Create Order — Insufficient Stock Log

Di method `CreateAsync`, pada block:

```csharp
if (product.StockQuantity < item.Quantity)
```

Sebelum throw, tambahkan:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.InsufficientStockDetected,
    productId: product.Id,
    errorCode: ErrorCodes.InsufficientStock,
    metadata: new
    {
        productId = product.Id,
        productName = product.Name,
        requestedQuantity = item.Quantity,
        availableQuantity = product.StockQuantity
    });
```

***

## 6.2 Create Order — After Commit Logs

Setelah:

```csharp
await transaction.CommitAsync(cancellationToken);
```

Tambahkan:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.OrderCreated,
    orderId: request.OrderId,
    orderNumber: orderNumber,
    afterState: new
    {
        status = OrderStatus.Pending.ToString(),
        totalAmount
    },
    metadata: new
    {
        customerId = request.CustomerId,
        itemCount = orderItems.Length
    });

foreach (var item in requestedItems)
{
    var product = productById[item.ProductId];

    _activityLogWriter.TryWrite(
        ActivityLogTypes.StockDeducted,
        orderId: request.OrderId,
        orderNumber: orderNumber,
        productId: product.Id,
        beforeState: new
        {
            stockQuantity = product.StockQuantity
        },
        afterState: new
        {
            stockQuantity = product.StockQuantity - item.Quantity
        },
        metadata: new
        {
            quantity = item.Quantity,
            productName = product.Name
        });
}
```

***

## 6.3 Update Status — Rejected Rule Log

Di method `UpdateStatusAsync`, saat:

```csharp
if (!ruleResult.IsAllowed)
```

Sebelum throw, tambahkan:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.OrderStatusRejected,
    orderId: order.Id,
    orderNumber: order.OrderNumber,
    errorCode: ruleResult.ErrorCode ?? ErrorCodes.InvalidOrderStatusTransition,
    beforeState: new
    {
        status = currentStatus.ToString(),
        rowVersion = order.RowVersion
    },
    afterState: new
    {
        targetStatus = request.TargetStatus.ToString()
    },
    metadata: new
    {
        reason = ruleResult.ErrorMessage,
        requestedBy = request.UpdatedBy,
        requestedByRole = request.UpdatedByRole.ToString()
    });
```

***

## 6.4 Update Status — After Commit Final Log

Setelah commit di `UpdateStatusAsync`:

```csharp
await transaction.CommitAsync(cancellationToken);
```

Tambahkan:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.OrderStatusChanged,
    orderId: order.Id,
    orderNumber: order.OrderNumber,
    beforeState: new
    {
        status = currentStatus.ToString(),
        rowVersion = order.RowVersion
    },
    afterState: new
    {
        status = request.TargetStatus.ToString(),
        rowVersion = nextRowVersion
    },
    metadata: new
    {
        reason = request.Reason,
        updatedBy = request.UpdatedBy,
        updatedByRole = request.UpdatedByRole.ToString()
    });
```

***

## 6.5 Cancel — Rejected Rule Log

Di method `CancelAsync`, saat:

```csharp
if (!ruleResult.IsAllowed)
```

Sebelum throw:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.OrderStatusRejected,
    orderId: order.Id,
    orderNumber: order.OrderNumber,
    errorCode: ruleResult.ErrorCode ?? ErrorCodes.InvalidOrderStatusTransition,
    beforeState: new
    {
        status = currentStatus.ToString(),
        rowVersion = order.RowVersion
    },
    afterState: new
    {
        targetStatus = OrderStatus.Cancelled.ToString()
    },
    metadata: new
    {
        reason = ruleResult.ErrorMessage,
        cancelledBy = request.CancelledBy,
        cancelledByRole = request.CancelledByRole.ToString(),
        cancellationReason = request.CancellationReason.ToString()
    });
```

***

## 6.6 Cancel — After Commit Logs

Setelah commit di `CancelAsync`:

```csharp
await transaction.CommitAsync(cancellationToken);
```

Tambahkan:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.OrderCancelled,
    orderId: order.Id,
    orderNumber: order.OrderNumber,
    beforeState: new
    {
        status = currentStatus.ToString(),
        rowVersion = order.RowVersion
    },
    afterState: new
    {
        status = OrderStatus.Cancelled.ToString(),
        rowVersion = nextRowVersion
    },
    metadata: new
    {
        cancellationReason = request.CancellationReason.ToString(),
        restoreStock = request.RestoreStock,
        cancelledBy = request.CancelledBy,
        cancelledByRole = request.CancelledByRole.ToString(),
        paymentRefundRequired = refundRequired
    });

foreach (var item in stockRestored)
{
    _activityLogWriter.TryWrite(
        ActivityLogTypes.StockRestored,
        orderId: order.Id,
        orderNumber: order.OrderNumber,
        productId: item.ProductId,
        metadata: new
        {
            item.Quantity,
            cancellationReason = request.CancellationReason.ToString()
        });
}

foreach (var item in stockNotRestored)
{
    _activityLogWriter.TryWrite(
        ActivityLogTypes.StockNotRestored,
        orderId: order.Id,
        orderNumber: order.OrderNumber,
        productId: item.ProductId,
        metadata: new
        {
            item.Quantity,
            item.Reason,
            cancellationReason = request.CancellationReason.ToString()
        });
}
```

***

## 6.7 Cancel — Payment RefundRequired Log

Setelah `refundRequired` dihitung, atau setelah commit:

```csharp
if (refundRequired)
{
    _activityLogWriter.TryWrite(
        ActivityLogTypes.PaymentRefundRequired,
        orderId: order.Id,
        orderNumber: order.OrderNumber,
        metadata: new
        {
            reason = "Order cancelled after payment was paid.",
            cancellationReason = request.CancellationReason.ToString()
        });
}
```

Taruh setelah commit supaya hanya tercatat kalau transaction sukses.

***

# 7. Update PaymentRepository — Payment Events

File:

```text
src/OrderManagement.Infrastructure/Repositories/PaymentRepository.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

Update constructor:

```csharp
private readonly IDbConnectionFactory _connectionFactory;
private readonly IOrderRulesService _orderRulesService;
private readonly IActivityLogWriter _activityLogWriter;

public PaymentRepository(
    IDbConnectionFactory connectionFactory,
    IOrderRulesService orderRulesService,
    IActivityLogWriter activityLogWriter)
{
    _connectionFactory = connectionFactory;
    _orderRulesService = orderRulesService;
    _activityLogWriter = activityLogWriter;
}
```

***

## 7.1 Payment Rejected Log

Di method `CreateAsync`, saat:

```csharp
if (!ruleResult.IsAllowed)
```

Sebelum throw:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.PaymentRejected,
    orderId: order.Id,
    orderNumber: order.OrderNumber,
    errorCode: ruleResult.ErrorCode ?? ErrorCodes.PaymentNotAllowed,
    beforeState: new
    {
        orderStatus = currentOrderStatus.ToString(),
        rowVersion = order.RowVersion
    },
    metadata: new
    {
        reason = ruleResult.ErrorMessage,
        requestedBy = request.RequestedBy,
        requestedByRole = request.RequestedByRole.ToString(),
        hasExistingPaidPayment
    });
```

***

## 7.2 Payment Final Logs After Commit

Setelah:

```csharp
await transaction.CommitAsync(cancellationToken);
```

Tambahkan:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.PaymentCreated,
    orderId: order.Id,
    orderNumber: order.OrderNumber,
    paymentId: paymentId,
    afterState: new
    {
        paymentStatus = paymentStatus.ToString(),
        orderStatus = finalOrderStatus.ToString()
    },
    metadata: new
    {
        amount = order.TotalAmount,
        provider = request.Provider,
        paymentReference,
        requestedBy = request.RequestedBy,
        requestedByRole = request.RequestedByRole.ToString()
    });

if (paymentStatus == PaymentStatus.Paid)
{
    _activityLogWriter.TryWrite(
        ActivityLogTypes.PaymentPaid,
        orderId: order.Id,
        orderNumber: order.OrderNumber,
        paymentId: paymentId,
        beforeState: new
        {
            orderStatus = OrderStatus.Pending.ToString(),
            rowVersion = order.RowVersion
        },
        afterState: new
        {
            orderStatus = OrderStatus.Confirmed.ToString(),
            rowVersion = nextRowVersion
        },
        metadata: new
        {
            amount = order.TotalAmount,
            provider = request.Provider,
            paymentReference
        });
}
else
{
    _activityLogWriter.TryWrite(
        ActivityLogTypes.PaymentFailed,
        orderId: order.Id,
        orderNumber: order.OrderNumber,
        paymentId: paymentId,
        metadata: new
        {
            amount = order.TotalAmount,
            provider = request.Provider,
            paymentReference
        });
}
```

***

# 8. Update PaymentService — Optional Attempt Log

File:

```text
src/OrderManagement.Application/Services/PaymentService.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

Tambahkan field:

```csharp
private readonly IActivityLogWriter _activityLogWriter;
```

Update constructor:

```csharp
IActivityLogWriter activityLogWriter,
```

Set field:

```csharp
_activityLogWriter = activityLogWriter;
```

Di awal `CreateAsync` setelah validation and auth role parse:

```csharp
_activityLogWriter.TryWrite(
    ActivityLogTypes.PaymentCreated,
    orderId: command.OrderId,
    metadata: new
    {
        provider = command.Provider,
        simulateResult = command.SimulateResult,
        requestedBy = currentUserId,
        requestedByRole = currentRole.ToString(),
        stage = "Attempt"
    });
```

> Final payment result tetap dicatat di repository after commit.

***

# 9. Update ActivityLogTypes — Add Attempt Types Optional

File:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogTypes.cs
```

Tambahkan kalau mau lebih eksplisit:

```csharp
public const string PaymentCreateStarted = "PaymentCreateStarted";
public const string OrderStatusChangeStarted = "OrderStatusChangeStarted";
public const string OrderCancelStarted = "OrderCancelStarted";
```

Kalau lu tambahkan ini, di OrderService/PaymentService lebih bagus pakai:

```csharp
ActivityLogTypes.PaymentCreateStarted
ActivityLogTypes.OrderStatusChangeStarted
ActivityLogTypes.OrderCancelStarted
```

Daripada reuse `PaymentCreated`/`OrderCancelled` untuk attempt.

## Rekomendasi final

Update `ActivityLogTypes.cs` jadi include:

```csharp
public const string OrderStatusChangeStarted = "OrderStatusChangeStarted";
public const string OrderCancelStarted = "OrderCancelStarted";
public const string PaymentCreateStarted = "PaymentCreateStarted";
```

Lalu di OrderService ganti:

```csharp
ActivityLogTypes.OrderStatusChanged
```

untuk attempt menjadi:

```csharp
ActivityLogTypes.OrderStatusChangeStarted
```

Ganti cancel attempt menjadi:

```csharp
ActivityLogTypes.OrderCancelStarted
```

Ganti payment attempt menjadi:

```csharp
ActivityLogTypes.PaymentCreateStarted
```

Ini lebih clean.

***

# 10. Build Check

Run:

```bash
dotnet build
```

Kemungkinan compile issue yang mungkin muncul:

## 10.1 Constructor dependency berubah

Karena kita menambah `IActivityLogWriter` ke:

```text
AuthService
OrderService
PaymentService
OrderRepository
PaymentRepository
IdempotencyService
```

DI sudah punya:

```csharp
services.AddScoped<IActivityLogWriter, ActivityLogWriter>();
```

Jadi harus aman.

## 10.2 Missing using

Pastikan file yang pakai log writer ada:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
```

***

# 11. Run API

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

***

# 12. Smoke Test

## Login admin

```bash
ADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: log-login-admin-001" \
  -d '{"username":"admin","password":"Password123!"}')

ADMIN_TOKEN=$(echo "$ADMIN_LOGIN" | jq -r '.accessToken')
```

Wait 1 second, then check:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT correlation_id, activity_type, actor_username, actor_role, metadata, created_at FROM activity_logs ORDER BY created_at DESC LIMIT 10;"
```

Expected:

```text
LoginSucceeded
RequestCompleted
```

***

## Create order

```bash
CUSTOMER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}')

CUSTOMER_TOKEN=$(echo "$CUSTOMER_LOGIN" | jq -r '.accessToken')
CUSTOMER_ID=$(echo "$CUSTOMER_LOGIN" | jq -r '.user.id')

PRODUCT_ID=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT id FROM products WHERE sku = 'PRD-MOUSE-001' LIMIT 1;" \
  | xargs)

curl -k -s -X POST https://localhost:7000/api/v1/orders \
  -H "Authorization: Bearer $CUSTOMER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "X-Correlation-ID: log-create-order-001" \
  -d "{
    \"customerId\": \"$CUSTOMER_ID\",
    \"items\": [
      {
        \"productId\": \"$PRODUCT_ID\",
        \"quantity\": 1
      }
    ],
    \"shippingAddress\": \"Jl. Activity Log Test\"
  }" | jq
```

Check logs:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT correlation_id, activity_type, order_number, product_id, metadata FROM activity_logs WHERE correlation_id = 'log-create-order-001' ORDER BY created_at;"
```

Expected activity sequence roughly:

```text
RequestCompleted
OrderCreateStarted
IdempotencyAccepted
OrderCreated
StockDeducted
```

Order bisa beda urutan sedikit karena queue async, tapi correlation id sama.

***

# 13. Security Verification

Pastikan query ini tidak menemukan token/password:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT id, metadata FROM activity_logs WHERE metadata::text ILIKE '%Password123%' OR metadata::text ILIKE '%Bearer%';"
```

Expected:

```text
0 rows
```

***

# 14. Performance Note

Activity logs sekarang:

```text
- masuk memory queue via TryWrite
- request tidak await database insert
- background worker batch insert
- queue bounded
```

Jadi overhead request path hanya:

```text
- build small object
- serialize small metadata JSON
- TryWrite channel
```

Untuk metadata besar, tetap jangan dipakai.

***

# 15. Commit Batch 14B

```bash
git add .
git commit -m "feat: emit activity logs from request and business flows"
```

***