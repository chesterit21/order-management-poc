Siap bro. Gue sudah buatin/update dokumen Markdown di folder `docs/`:

```text
docs/next-checklist.md
docs/technical-design.md
docs/api-contract.md
docs/concurrency-design.md
docs/error-handling.md
docs/hardening-notes.md
docs/idempotency-design.md
docs/logging-design.md
docs/test-report.md
```

Isi dokumennya sudah cover:

```text
- Checklist lanjutan
- Hal yang harus sesuai saat testing
- Hal yang tidak boleh terjadi
- API contract lengkap
- Concurrency design
- Error handling
- Idempotency
- Logging design
- Hardening notes
- Technical design
- Test report
```

Dan lu benar banget soal logging page. Kalau mau production-grade, logging jangan cuma `ILogger` console. Kita perlu pisahkan:

```text
1. Technical log
2. Business activity log
3. Audit/trace log yang searchable
4. UI/page internal untuk tracing
```

Tapi juga **jangan semua response/request body dilog synchronous**, karena itu bisa bikin system berat dan risk bocor data. Solusi yang proper: **Activity Log Queue + Background Worker + bounded queue + internal log viewer**.

***

# Rencana Lanjutan: 3 Batch Logging & Observability

Gue sarankan kita lanjut dengan 3 batch tambahan.

***

# Batch 14A — Activity Log Queue Infrastructure

## Tujuan

Bikin fondasi logging activity yang tidak memberatkan request.

Implement:

```text
- activity_logs table migration
- ActivityLog domain/application model
- IActivityLogQueue
- BoundedChannelActivityLogQueue
- ActivityLogBackgroundWorker
- ActivityLogRepository batch insert
- ActivityLogOptions
- DI registration
```

## Kenapa pakai queue?

Karena kalau setiap proses request langsung insert log satu-satu ke DB secara synchronous, nanti:

```text
- latency request naik
- DB write makin ramai
- throughput turun
- kalau log DB lambat, API ikut lambat
```

Dengan queue:

```text
Request thread:
  Enqueue log cepat ke memory channel

Background worker:
  Batch insert log ke DB
```

## Policy

```text
Critical audit:
  Tetap harus aman, bisa dicatat transaction-bound atau enqueue wajib.

Trace/debug:
  Boleh async queue.

Jika queue penuh:
  Non-critical log boleh drop + metric counter.
  Critical log jangan silent drop.
```

## Table yang perlu dibuat

```text
activity_logs
- id
- correlation_id
- activity_type
- actor_user_id
- actor_role
- order_id
- order_number
- product_id
- payment_id
- request_path
- http_method
- status_code
- elapsed_ms
- before_state jsonb
- after_state jsonb
- metadata jsonb
- created_at
```

Index:

```text
correlation_id
order_id
activity_type
created_at
actor_user_id
```

***

# Batch 14B — Emit Activity Logs di Business Flow

## Tujuan

Pasang activity logs di proses penting.

Implement log activity untuk:

```text
Auth:
- LoginSucceeded
- LoginFailed

Product:
- ProductListViewed optional
- ProductDetailViewed optional

Order:
- CreateOrderStarted
- OrderCreated
- StockDeducted
- InsufficientStockDetected
- IdempotencyReplayReturned
- IdempotencyConflict
- OrderStatusChanged
- OrderStatusRejected
- OrderCancelled
- StockRestored
- StockNotRestored

Payment:
- PaymentCreated
- PaymentPaid
- PaymentFailed
- PaymentRejected
- PaymentRefundRequired

Concurrency:
- RowVersionConflict
- LockTimeout
- DuplicatePaymentPrevented
```

## Data yang boleh dilog

Allowed:

```text
CorrelationId
UserId
Role
OrderId
OrderNumber
ProductId
PaymentId
Quantity
StockBefore
StockAfter
StatusBefore
StatusAfter
ErrorCode
ElapsedMs
```

Tidak boleh:

```text
Password
Password hash
JWT token
Authorization header
Full login body
Connection string
Sensitive full request/response body
```

## Request/Response Logging

Kita jangan log full response body by default.

Yang proper:

```text
Request:
- method
- path
- query minimal
- user id
- role
- correlation id

Response:
- status code
- elapsed ms
- error code jika ada
```

Optional untuk debug local:

```text
Log body hanya untuk non-sensitive endpoint dan development only.
```

***

# Batch 14C — Internal Logging API + Display Page

## Tujuan

Bikin halaman internal untuk tracing log.

Implement:

```text
GET /api/v1/internal/logs
GET /api/v1/internal/logs/{id}
GET /api/v1/internal/logs/timeline
GET /internal/logs page sederhana
```

Access:

```text
Admin/Ops only
```

Filter:

```text
correlationId
orderId
orderNumber
activityType
actorUserId
fromDate
toDate
statusCode
page
pageSize
```

Page display:

```text
- Search box correlationId/orderId
- Timeline view
- Activity type badge
- Actor
- Request path
- Status code
- Elapsed ms
- Before/after state expandable JSON
- Metadata expandable JSON
```

## Kenapa perlu page?

Karena pas ops bilang:

> “Order ini error kenapa?”

Admin/Ops bisa cari:

```text
CorrelationId
OrderId
OrderNumber
```

Lalu lihat timeline:

```text
RequestStarted
CreateOrderStarted
StockDeducted
OrderCreated
PaymentPaid
OrderConfirmed
```

atau kalau error:

```text
CreateOrderStarted
InsufficientStockDetected
RequestFailed
```

***

# Checklist Tambahan Khusus Logging

Ini yang harus sesuai nanti setelah batch logging:

## Harus Terjadi

```text
- Setiap request punya correlation id.
- Activity penting masuk activity_logs.
- Log bisa dicari by correlation id.
- Log bisa dicari by order id.
- Logging tidak menyimpan password/JWT.
- Queue tidak membuat request lambat.
- Background worker batch insert.
- Kalau background insert gagal, error tercatat technical log.
- Admin/Ops bisa lihat log timeline.
```

## Tidak Boleh Terjadi

```text
- Full Authorization header tersimpan.
- Password/request login body tersimpan.
- Response body besar tersimpan terus-menerus.
- Queue unbounded sampai memory naik terus.
- Log DB lambat membuat endpoint order lambat.
- Customer bisa akses internal logs.
- Activity logs tidak punya retention strategy.
- Sensitive metadata masuk JSONB.
```

***

# Update Dokumen yang Sudah Gue Buat

## `docs/next-checklist.md`

Berisi:

```text
- checklist functional
- checklist non-functional
- hal yang tidak boleh terjadi
- final checklist before submission
```

## `docs/api-contract.md`

Berisi:

```text
- auth contract
- products
- orders
- status update
- cancel
- payment
- response/error summary
```

## `docs/concurrency-design.md`

Berisi:

```text
- concurrent stock deduction
- idempotent create race
- concurrent status update
- double cancel
- cancel restore/no restore
- payment vs cancel
- duplicate payment
```

## `docs/error-handling.md`

Berisi:

```text
- standard error format
- exception mapping
- HTTP status mapping
- error codes
- client UX handling
```

## `docs/hardening-notes.md`

Berisi:

```text
- order number sequence
- idempotency hardening
- logging hardening
- security hardening
- DB/API/observability hardening
```

## `docs/idempotency-design.md`

Berisi:

```text
- purpose
- header
- scope
- request hash
- table
- behavior matrix
- race handling
- replay response
- client responsibility
```

## `docs/logging-design.md`

Berisi:

```text
- correlation id
- technical logs
- business activity logs
- sensitive data rules
- async queue design
- activity_logs table proposal
- internal logging page/API proposal
```

## `docs/technical-design.md`

Berisi:

```text
- architecture
- layers
- modules
- transaction boundaries
- operational concerns
- known limitations
```

## `docs/test-report.md`

Berisi:

```text
- unit test coverage
- integration test coverage
- expected result
- must-not-happen cases
- acceptance criteria
```

***

Gas bro. Kita lanjut **Batch 14A: Activity Log Queue Infrastructure**.

Batch ini adalah foundation untuk tracing activity log yang **tidak memberatkan request path**. Jadi nanti di Batch 14B kita tinggal panggil queue dari flow bisnis seperti order/payment/cancel/idempotency.

## Target Batch 14A

Implement:

```text
- migration activity_logs
- ActivityLogMessage
- IActivityLogQueue
- IActivityLogRepository
- BoundedChannelActivityLogQueue
- ActivityLogBackgroundWorker
- ActivityLogRepository batch insert
- ActivityLogOptions
- DI registration
- appsettings config
```

Design-nya:

```text
Request thread
    |
    | TryEnqueue ActivityLogMessage
    v
Bounded Channel in memory
    |
    v
BackgroundService
    |
    | Batch insert
    v
PostgreSQL activity_logs
```

Kenapa begini?

```text
- Request tidak menunggu insert log ke DB.
- Queue bounded supaya memory tidak liar.
- Background worker insert batch untuk performance.
- Jika queue penuh, non-critical log bisa drop dan tercatat technical log.
```

***

# Batch 14A — Activity Log Queue Infrastructure

***

# 1. Migration: `activity_logs`

Create file baru:

```text
db/migrations/013_create_activity_logs.sql
```

Isi:

```sql
CREATE TABLE IF NOT EXISTS activity_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    correlation_id VARCHAR(100) NOT NULL,
    activity_type VARCHAR(100) NOT NULL,

    actor_user_id UUID NULL,
    actor_username VARCHAR(100) NULL,
    actor_role VARCHAR(50) NULL,

    order_id UUID NULL,
    order_number VARCHAR(50) NULL,
    product_id UUID NULL,
    payment_id UUID NULL,

    request_path VARCHAR(500) NULL,
    http_method VARCHAR(20) NULL,
    status_code INT NULL,
    elapsed_ms BIGINT NULL,

    error_code VARCHAR(100) NULL,

    before_state JSONB NULL,
    after_state JSONB NULL,
    metadata JSONB NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_activity_logs_correlation_id
ON activity_logs(correlation_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_activity_type
ON activity_logs(activity_type);

CREATE INDEX IF NOT EXISTS idx_activity_logs_actor_user_id
ON activity_logs(actor_user_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_order_id
ON activity_logs(order_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_order_number
ON activity_logs(order_number);

CREATE INDEX IF NOT EXISTS idx_activity_logs_product_id
ON activity_logs(product_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_payment_id
ON activity_logs(payment_id);

CREATE INDEX IF NOT EXISTS idx_activity_logs_created_at
ON activity_logs(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_activity_logs_error_code
ON activity_logs(error_code)
WHERE error_code IS NOT NULL;
```

## Notes

Index by:

```text
correlation_id  -> tracing request
order_id        -> tracing order timeline
order_number    -> tracing by display number
activity_type   -> filter event
created_at      -> recent logs
error_code      -> error investigation
```

***

# 2. Application DTO: `ActivityLogMessage`

Buat folder:

```bash
mkdir -p src/OrderManagement.Application/DTOs/ActivityLogs
```

Create file:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogMessage.cs
```

Isi:

```csharp
namespace OrderManagement.Application.DTOs.ActivityLogs;

public sealed class ActivityLogMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string CorrelationId { get; init; }

    public required string ActivityType { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? ActorUsername { get; init; }

    public string? ActorRole { get; init; }

    public Guid? OrderId { get; init; }

    public string? OrderNumber { get; init; }

    public Guid? ProductId { get; init; }

    public Guid? PaymentId { get; init; }

    public string? RequestPath { get; init; }

    public string? HttpMethod { get; init; }

    public int? StatusCode { get; init; }

    public long? ElapsedMs { get; init; }

    public string? ErrorCode { get; init; }

    /// <summary>
    /// JSON string. Must be valid JSON if provided.
    /// </summary>
    public string? BeforeStateJson { get; init; }

    /// <summary>
    /// JSON string. Must be valid JSON if provided.
    /// </summary>
    public string? AfterStateJson { get; init; }

    /// <summary>
    /// JSON string. Must be valid JSON if provided.
    /// </summary>
    public string? MetadataJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

***

# 3. Activity Log Constants

Create file:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogTypes.cs
```

Isi:

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
    public const string OrderStatusChanged = "OrderStatusChanged";
    public const string OrderStatusRejected = "OrderStatusRejected";
    public const string OrderCancelled = "OrderCancelled";

    public const string StockDeducted = "StockDeducted";
    public const string StockRestored = "StockRestored";
    public const string StockNotRestored = "StockNotRestored";
    public const string InsufficientStockDetected = "InsufficientStockDetected";

    public const string PaymentCreated = "PaymentCreated";
    public const string PaymentPaid = "PaymentPaid";
    public const string PaymentFailed = "PaymentFailed";
    public const string PaymentRejected = "PaymentRejected";
    public const string PaymentRefundRequired = "PaymentRefundRequired";

    public const string ConcurrencyConflict = "ConcurrencyConflict";
}
```

***

# 4. Activity Log JSON Helper

Biar nanti Batch 14B gampang bikin metadata JSON tanpa bocorin data sensitive.

Create file:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogJson.cs
```

Isi:

```csharp
using System.Text.Json;

namespace OrderManagement.Application.DTOs.ActivityLogs;

public static class ActivityLogJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static string? SerializeOrNull(object? value)
    {
        return value is null
            ? null
            : JsonSerializer.Serialize(value, JsonOptions);
    }
}
```

***

# 5. Application Abstractions

Buat folder:

```bash
mkdir -p src/OrderManagement.Application/Abstractions/ActivityLogs
```

***

## 5.1 `IActivityLogQueue.cs`

Create file:

```text
src/OrderManagement.Application/Abstractions/ActivityLogs/IActivityLogQueue.cs
```

Isi:

```csharp
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogQueue
{
    bool TryEnqueue(ActivityLogMessage message);

    ValueTask EnqueueAsync(
        ActivityLogMessage message,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<ActivityLogMessage>> ReadBatchAsync(
        int maxBatchSize,
        TimeSpan maxWaitTime,
        CancellationToken cancellationToken = default);
}
```

Design:

```text
TryEnqueue:
  non-blocking, cocok untuk request path.

EnqueueAsync:
  bisa dipakai untuk critical event jika mau menunggu slot.

ReadBatchAsync:
  dipakai background worker untuk batch insert.
```

***

## 5.2 `IActivityLogRepository.cs`

Create file:

```text
src/OrderManagement.Application/Abstractions/ActivityLogs/IActivityLogRepository.cs
```

Isi:

```csharp
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogRepository
{
    Task InsertBatchAsync(
        IReadOnlyCollection<ActivityLogMessage> messages,
        CancellationToken cancellationToken = default);
}
```

***

# 6. Infrastructure Options

## `ActivityLogOptions.cs`

Create file:

```text
src/OrderManagement.Infrastructure/Options/ActivityLogOptions.cs
```

Isi:

```csharp
namespace OrderManagement.Infrastructure.Options;

public sealed class ActivityLogOptions
{
    public const string SectionName = "ActivityLog";

    public bool Enabled { get; init; } = true;

    public int QueueCapacity { get; init; } = 10_000;

    public int MaxBatchSize { get; init; } = 100;

    public int FlushIntervalMilliseconds { get; init; } = 1_000;

    public bool DropWhenQueueFull { get; init; } = true;
}
```

***

# 7. Queue Implementation

Buat folder:

```bash
mkdir -p src/OrderManagement.Infrastructure/ActivityLogs
```

## `BoundedChannelActivityLogQueue.cs`

Create file:

```text
src/OrderManagement.Infrastructure/ActivityLogs/BoundedChannelActivityLogQueue.cs
```

Isi:

```csharp
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class BoundedChannelActivityLogQueue : IActivityLogQueue
{
    private readonly Channel<ActivityLogMessage> _channel;
    private readonly ActivityLogOptions _options;
    private readonly ILogger<BoundedChannelActivityLogQueue> _logger;

    private long _droppedMessages;

    public BoundedChannelActivityLogQueue(
        IOptions<ActivityLogOptions> options,
        ILogger<BoundedChannelActivityLogQueue> logger)
    {
        _options = options.Value;
        _logger = logger;

        var capacity = _options.QueueCapacity <= 0
            ? 10_000
            : _options.QueueCapacity;

        _channel = Channel.CreateBounded<ActivityLogMessage>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = _options.DropWhenQueueFull
                    ? BoundedChannelFullMode.DropWrite
                    : BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
    }

    public bool TryEnqueue(ActivityLogMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.Enabled)
        {
            return false;
        }

        var written = _channel.Writer.TryWrite(message);

        if (!written)
        {
            var dropped = Interlocked.Increment(ref _droppedMessages);

            if (dropped % 100 == 1)
            {
                _logger.LogWarning(
                    "Activity log queue is full. DroppedMessages={DroppedMessages}",
                    dropped);
            }
        }

        return written;
    }

    public async ValueTask EnqueueAsync(
        ActivityLogMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.Enabled)
        {
            return;
        }

        await _channel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ActivityLogMessage>> ReadBatchAsync(
        int maxBatchSize,
        TimeSpan maxWaitTime,
        CancellationToken cancellationToken = default)
    {
        if (maxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize), "Max batch size must be greater than zero.");
        }

        var batch = new List<ActivityLogMessage>(maxBatchSize);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(maxWaitTime);

        try
        {
            while (batch.Count < maxBatchSize &&
                   await _channel.Reader.WaitToReadAsync(timeoutCts.Token))
            {
                while (batch.Count < maxBatchSize &&
                       _channel.Reader.TryRead(out var message))
                {
                    batch.Add(message);
                }

                if (batch.Count > 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout reached. Return whatever has been collected.
        }

        return batch;
    }
}
```

## Note bro

`FullMode = DropWrite` dipakai kalau `DropWhenQueueFull = true`. Ini cocok untuk **non-critical trace log**. Untuk critical audit, nanti Batch 14B kita bisa pakai `EnqueueAsync` atau transaction-bound insert jika event wajib tidak boleh hilang.

***

# 8. ActivityLogRepository

## `ActivityLogRepository.cs`

Create file:

```text
src/OrderManagement.Infrastructure/ActivityLogs/ActivityLogRepository.cs
```

Isi:

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class ActivityLogRepository : IActivityLogRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ActivityLogRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertBatchAsync(
        IReadOnlyCollection<ActivityLogMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var parameters = new DynamicParameters();
        var values = new List<string>(messages.Count);

        var index = 0;

        foreach (var message in messages)
        {
            var prefix = $"p{index}";

            values.Add($"""
                        (
                            @{prefix}Id,
                            @{prefix}CorrelationId,
                            @{prefix}ActivityType,
                            @{prefix}ActorUserId,
                            @{prefix}ActorUsername,
                            @{prefix}ActorRole,
                            @{prefix}OrderId,
                            @{prefix}OrderNumber,
                            @{prefix}ProductId,
                            @{prefix}PaymentId,
                            @{prefix}RequestPath,
                            @{prefix}HttpMethod,
                            @{prefix}StatusCode,
                            @{prefix}ElapsedMs,
                            @{prefix}ErrorCode,
                            CAST(@{prefix}BeforeStateJson AS jsonb),
                            CAST(@{prefix}AfterStateJson AS jsonb),
                            CAST(@{prefix}MetadataJson AS jsonb),
                            @{prefix}CreatedAt
                        )
                        """);

            parameters.Add($"{prefix}Id", message.Id);
            parameters.Add($"{prefix}CorrelationId", TrimToLength(message.CorrelationId, 100));
            parameters.Add($"{prefix}ActivityType", TrimToLength(message.ActivityType, 100));
            parameters.Add($"{prefix}ActorUserId", message.ActorUserId);
            parameters.Add($"{prefix}ActorUsername", TrimToLength(message.ActorUsername, 100));
            parameters.Add($"{prefix}ActorRole", TrimToLength(message.ActorRole, 50));
            parameters.Add($"{prefix}OrderId", message.OrderId);
            parameters.Add($"{prefix}OrderNumber", TrimToLength(message.OrderNumber, 50));
            parameters.Add($"{prefix}ProductId", message.ProductId);
            parameters.Add($"{prefix}PaymentId", message.PaymentId);
            parameters.Add($"{prefix}RequestPath", TrimToLength(message.RequestPath, 500));
            parameters.Add($"{prefix}HttpMethod", TrimToLength(message.HttpMethod, 20));
            parameters.Add($"{prefix}StatusCode", message.StatusCode);
            parameters.Add($"{prefix}ElapsedMs", message.ElapsedMs);
            parameters.Add($"{prefix}ErrorCode", TrimToLength(message.ErrorCode, 100));
            parameters.Add($"{prefix}BeforeStateJson", NormalizeJsonOrNull(message.BeforeStateJson));
            parameters.Add($"{prefix}AfterStateJson", NormalizeJsonOrNull(message.AfterStateJson));
            parameters.Add($"{prefix}MetadataJson", NormalizeJsonOrNull(message.MetadataJson));
            parameters.Add($"{prefix}CreatedAt", message.CreatedAt);

            index++;
        }

        var sql = $"""
                   INSERT INTO activity_logs
                       (
                           id,
                           correlation_id,
                           activity_type,
                           actor_user_id,
                           actor_username,
                           actor_role,
                           order_id,
                           order_number,
                           product_id,
                           payment_id,
                           request_path,
                           http_method,
                           status_code,
                           elapsed_ms,
                           error_code,
                           before_state,
                           after_state,
                           metadata,
                           created_at
                       )
                   VALUES
                       {string.Join(",\n", values)};
                   """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                parameters,
                cancellationToken: cancellationToken));
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    private static string? NormalizeJsonOrNull(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : json;
    }
}
```

## Kenapa batch insert dynamic SQL?

Karena untuk batch kecil-menengah, ini cukup cepat dan menghindari insert satu-satu. `MaxBatchSize` default 100, jadi SQL tidak akan membengkak liar.

***

# 9. Background Worker

## `ActivityLogBackgroundWorker.cs`

Create file:

```text
src/OrderManagement.Infrastructure/ActivityLogs/ActivityLogBackgroundWorker.cs
```

Isi:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class ActivityLogBackgroundWorker : BackgroundService
{
    private readonly IActivityLogQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActivityLogOptions _options;
    private readonly ILogger<ActivityLogBackgroundWorker> _logger;

    public ActivityLogBackgroundWorker(
        IActivityLogQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<ActivityLogOptions> options,
        ILogger<ActivityLogBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Activity log background worker is disabled.");
            return;
        }

        var maxBatchSize = _options.MaxBatchSize <= 0
            ? 100
            : _options.MaxBatchSize;

        var flushInterval = TimeSpan.FromMilliseconds(
            _options.FlushIntervalMilliseconds <= 0
                ? 1_000
                : _options.FlushIntervalMilliseconds);

        _logger.LogInformation(
            "Activity log background worker started. MaxBatchSize={MaxBatchSize} FlushIntervalMs={FlushIntervalMs}",
            maxBatchSize,
            flushInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _queue.ReadBatchAsync(
                    maxBatchSize,
                    flushInterval,
                    stoppingToken);

                if (batch.Count == 0)
                {
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();

                var repository = scope.ServiceProvider.GetRequiredService<IActivityLogRepository>();

                await repository.InsertBatchAsync(batch, stoppingToken);

                _logger.LogDebug(
                    "Activity log batch inserted. Count={Count}",
                    batch.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown.
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to process activity log batch.");

                await DelayAfterFailureAsync(stoppingToken);
            }
        }

        _logger.LogInformation("Activity log background worker stopped.");
    }

    private static async Task DelayAfterFailureAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ignore.
        }
    }
}
```

***

# 10. Current Request Activity Context Helper

Ini belum emit log, tapi nanti Batch 14B akan butuh helper untuk baca correlation/user/path.

Buat file:

```text
src/OrderManagement.Application/Abstractions/ActivityLogs/IActivityLogContextAccessor.cs
```

Isi:

```csharp
namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogContextAccessor
{
    string CorrelationId { get; }

    string? RequestPath { get; }

    string? HttpMethod { get; }

    Guid? UserId { get; }

    string? Username { get; }

    string? Role { get; }
}
```

Create implementation:

```text
src/OrderManagement.Infrastructure/ActivityLogs/HttpActivityLogContextAccessor.cs
```

Isi:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OrderManagement.Application.Abstractions.ActivityLogs;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class HttpActivityLogContextAccessor : IActivityLogContextAccessor
{
    private const string CorrelationIdItemName = "CorrelationId";
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpActivityLogContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string CorrelationId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;

            if (context is null)
            {
                return Guid.NewGuid().ToString("N");
            }

            if (context.Items.TryGetValue(CorrelationIdItemName, out var value) &&
                value is string correlationId &&
                !string.IsNullOrWhiteSpace(correlationId))
            {
                return correlationId;
            }

            if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var values))
            {
                var headerValue = values.FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(headerValue))
                {
                    return headerValue.Trim();
                }
            }

            return Guid.NewGuid().ToString("N");
        }
    }

    public string? RequestPath =>
        _httpContextAccessor.HttpContext?.Request.Path.Value;

    public string? HttpMethod =>
        _httpContextAccessor.HttpContext?.Request.Method;

    public Guid? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;

            var value =
                principal?.FindFirstValue(ClaimTypes.NameIdentifier) ??
                principal?.FindFirstValue("sub");

            return Guid.TryParse(value, out var userId)
                ? userId
                : null;
        }
    }

    public string? Username =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("username");

    public string? Role =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("role") ??
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);
}
```

***

# 11. Activity Log Writer Helper

Ini service kecil supaya Batch 14B gampang emit log tanpa bikin object manual terus.

Create abstraction:

```text
src/OrderManagement.Application/Abstractions/ActivityLogs/IActivityLogWriter.cs
```

Isi:

```csharp
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogWriter
{
    bool TryWrite(ActivityLogMessage message);

    bool TryWrite(
        string activityType,
        Guid? orderId = null,
        string? orderNumber = null,
        Guid? productId = null,
        Guid? paymentId = null,
        int? statusCode = null,
        long? elapsedMs = null,
        string? errorCode = null,
        object? beforeState = null,
        object? afterState = null,
        object? metadata = null);
}
```

Create implementation:

```text
src/OrderManagement.Infrastructure/ActivityLogs/ActivityLogWriter.cs
```

Isi:

```csharp
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class ActivityLogWriter : IActivityLogWriter
{
    private readonly IActivityLogQueue _queue;
    private readonly IActivityLogContextAccessor _contextAccessor;
    private readonly ILogger<ActivityLogWriter> _logger;

    public ActivityLogWriter(
        IActivityLogQueue queue,
        IActivityLogContextAccessor contextAccessor,
        ILogger<ActivityLogWriter> logger)
    {
        _queue = queue;
        _contextAccessor = contextAccessor;
        _logger = logger;
    }

    public bool TryWrite(ActivityLogMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var written = _queue.TryEnqueue(message);

        if (!written)
        {
            _logger.LogWarning(
                "Failed to enqueue activity log. ActivityType={ActivityType} CorrelationId={CorrelationId}",
                message.ActivityType,
                message.CorrelationId);
        }

        return written;
    }

    public bool TryWrite(
        string activityType,
        Guid? orderId = null,
        string? orderNumber = null,
        Guid? productId = null,
        Guid? paymentId = null,
        int? statusCode = null,
        long? elapsedMs = null,
        string? errorCode = null,
        object? beforeState = null,
        object? afterState = null,
        object? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(activityType))
        {
            throw new ArgumentException("Activity type is required.", nameof(activityType));
        }

        var message = new ActivityLogMessage
        {
            CorrelationId = _contextAccessor.CorrelationId,
            ActivityType = activityType.Trim(),
            ActorUserId = _contextAccessor.UserId,
            ActorUsername = _contextAccessor.Username,
            ActorRole = _contextAccessor.Role,
            OrderId = orderId,
            OrderNumber = orderNumber,
            ProductId = productId,
            PaymentId = paymentId,
            RequestPath = _contextAccessor.RequestPath,
            HttpMethod = _contextAccessor.HttpMethod,
            StatusCode = statusCode,
            ElapsedMs = elapsedMs,
            ErrorCode = errorCode,
            BeforeStateJson = ActivityLogJson.SerializeOrNull(beforeState),
            AfterStateJson = ActivityLogJson.SerializeOrNull(afterState),
            MetadataJson = ActivityLogJson.SerializeOrNull(metadata),
            CreatedAt = DateTimeOffset.UtcNow
        };

        return TryWrite(message);
    }
}
```

***

# 12. DI Registration

Update file:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Infrastructure.ActivityLogs;
```

Lalu di method `AddInfrastructure`, tambahkan configure options:

```csharp
services.Configure<ActivityLogOptions>(
    configuration.GetSection(ActivityLogOptions.SectionName));
```

Tambahkan service registrations:

```csharp
services.AddSingleton<IActivityLogQueue, BoundedChannelActivityLogQueue>();
services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
services.AddScoped<IActivityLogContextAccessor, HttpActivityLogContextAccessor>();
services.AddScoped<IActivityLogWriter, ActivityLogWriter>();
services.AddHostedService<ActivityLogBackgroundWorker>();
```

## Full `DependencyInjection.cs` final

Kalau mau replace full file, pakai ini:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Infrastructure.ActivityLogs;
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

        services.Configure<ActivityLogOptions>(
            configuration.GetSection(ActivityLogOptions.SectionName));

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

        services.AddSingleton<IActivityLogQueue, BoundedChannelActivityLogQueue>();
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<IActivityLogContextAccessor, HttpActivityLogContextAccessor>();
        services.AddScoped<IActivityLogWriter, ActivityLogWriter>();
        services.AddHostedService<ActivityLogBackgroundWorker>();

        return services;
    }
}
```

***

# 13. Appsettings Config

Update:

```text
src/OrderManagement.Api/appsettings.json
```

Tambahkan section:

```json
"ActivityLog": {
  "Enabled": true,
  "QueueCapacity": 10000,
  "MaxBatchSize": 100,
  "FlushIntervalMilliseconds": 1000,
  "DropWhenQueueFull": true
}
```

Contoh posisi:

```json
{
  "Application": {
    "Name": "Order Management API",
    "DefaultTimezone": "UTC"
  },

  "ActivityLog": {
    "Enabled": true,
    "QueueCapacity": 10000,
    "MaxBatchSize": 100,
    "FlushIntervalMilliseconds": 1000,
    "DropWhenQueueFull": true
  }
}
```

***

## Development override

Update:

```text
src/OrderManagement.Api/appsettings.Development.json
```

Tambahkan:

```json
"ActivityLog": {
  "Enabled": true,
  "QueueCapacity": 5000,
  "MaxBatchSize": 50,
  "FlushIntervalMilliseconds": 500,
  "DropWhenQueueFull": true
}
```

***

## Testing override

Update:

```text
src/OrderManagement.Api/appsettings.Testing.json
```

Tambahkan:

```json
"ActivityLog": {
  "Enabled": true,
  "QueueCapacity": 1000,
  "MaxBatchSize": 25,
  "FlushIntervalMilliseconds": 200,
  "DropWhenQueueFull": true
}
```

***

# 14. Optional Smoke Test Endpoint

Agar Batch 14A bisa langsung diuji tanpa nunggu Batch 14B, kita buat endpoint internal sementara untuk enqueue log.

Create folder kalau belum ada:

```bash
mkdir -p src/OrderManagement.Api/Controllers/Internal
```

Create file:

```text
src/OrderManagement.Api/Controllers/Internal/InternalActivityLogsTestController.cs
```

Isi:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Api.Controllers.Internal;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOrOps)]
[Route("api/v1/internal/activity-logs/test")]
public sealed class InternalActivityLogsTestController : ControllerBase
{
    private readonly IActivityLogWriter _activityLogWriter;

    public InternalActivityLogsTestController(IActivityLogWriter activityLogWriter)
    {
        _activityLogWriter = activityLogWriter;
    }

    [HttpPost]
    public IActionResult EnqueueTestLog()
    {
        var written = _activityLogWriter.TryWrite(
            ActivityLogTypes.RequestCompleted,
            statusCode: StatusCodes.Status200OK,
            metadata: new
            {
                source = "manual-test",
                message = "Activity log test message"
            });

        return Ok(new
        {
            enqueued = written
        });
    }
}
```

> Ini endpoint internal Admin/Ops. Nanti Batch 14C kita bisa hapus/biarkan sebagai diagnostics internal.

***

# 15. Run Build

Run:

```bash
dotnet build
```

Kalau ada error missing namespace, pastikan file-file ini sudah ada:

```text
IActivityLogQueue.cs
IActivityLogRepository.cs
IActivityLogContextAccessor.cs
IActivityLogWriter.cs
ActivityLogMessage.cs
ActivityLogTypes.cs
ActivityLogJson.cs
ActivityLogOptions.cs
BoundedChannelActivityLogQueue.cs
ActivityLogRepository.cs
ActivityLogBackgroundWorker.cs
HttpActivityLogContextAccessor.cs
ActivityLogWriter.cs
```

***

# 16. Run API & Migration

Run:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

Startup akan apply:

```text
013_create_activity_logs.sql
```

Cek table:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "\\d activity_logs"
```

***

# 17. Smoke Test Activity Queue

Login admin:

```bash
ADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Password123!"}')

ADMIN_TOKEN=$(echo "$ADMIN_LOGIN" | jq -r '.accessToken')
```

Call test endpoint:

```bash
curl -k -i -X POST https://localhost:7000/api/v1/internal/activity-logs/test \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "X-Correlation-ID: activity-log-smoke-test-001"
```

Wait 1-2 detik, lalu cek DB:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT correlation_id, activity_type, actor_username, actor_role, metadata, created_at FROM activity_logs ORDER BY created_at DESC LIMIT 5;"
```

Expected:

```text
correlation_id = activity-log-smoke-test-001
activity_type = RequestCompleted
actor_username = admin
actor_role = Admin
metadata contains source/manual-test
```

***

# 18. Performance & Security Notes

Batch 14A sudah dibuat dengan perhatian berikut:

```text
- Queue bounded supaya memory tidak tumbuh tanpa batas.
- Background worker insert batch.
- Request path bisa pakai TryWrite non-blocking.
- Sensitive data tidak otomatis dilog.
- Metadata eksplisit dari caller, jadi Batch 14B harus tetap sanitize.
- Internal smoke endpoint protected Admin/Ops.
- JSONB fields support structured metadata.
- Index disiapkan untuk query tracing.
```

Hal yang harus dijaga di Batch 14B:

```text
- Jangan log password/token.
- Jangan log full request body login.
- Jangan log authorization header.
- Jangan log response body besar.
- Log business metadata seperlunya.
```

***

# 19. Commit Batch 14A

```bash
git add .
git commit -m "feat: add asynchronous activity log queue infrastructure"
```

***

