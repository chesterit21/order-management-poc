Gaskeun bro. Kita masuk **Batch 14C: Internal Logging API + Display Page**.

Batch ini akan membuat activity log bisa dicari dan ditampilkan untuk demo tracing:

```text
GET /api/v1/internal/activity-logs
GET /api/v1/internal/activity-logs/{id}
GET /internal/activity-logs
```

Access:

```text
Admin/Ops only
```

Fokus production-grade:

```text
- Query parameterized, no SQL injection.
- Pagination bounded.
- Tidak expose sensitive data.
- JSONB ditampilkan sebagai raw object/string aman.
- Internal endpoint protected Admin/Ops.
- Page HTML sederhana tanpa external dependency.
- Filter by correlationId/orderId/orderNumber/activityType/actorUserId/date range.
```

***

# Batch 14C — Internal Logging API + Display Page

***

# 1. Application DTOs

Buat folder:

```bash
mkdir -p src/OrderManagement.Application/DTOs/ActivityLogs
```

***

## 1.1 `ActivityLogQueryDto.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogQueryDto.cs
```

```csharp
namespace OrderManagement.Application.DTOs.ActivityLogs;

public sealed class ActivityLogQueryDto
{
    public string? CorrelationId { get; init; }

    public Guid? OrderId { get; init; }

    public string? OrderNumber { get; init; }

    public string? ActivityType { get; init; }

    public Guid? ActorUserId { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
```

***

## 1.2 `ActivityLogListItemDto.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogListItemDto.cs
```

```csharp
namespace OrderManagement.Application.DTOs.ActivityLogs;

public sealed class ActivityLogListItemDto
{
    public required Guid Id { get; init; }

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

    public string? MetadataJson { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
```

***

## 1.3 `ActivityLogDetailDto.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogDetailDto.cs
```

```csharp
namespace OrderManagement.Application.DTOs.ActivityLogs;

public sealed class ActivityLogDetailDto
{
    public required Guid Id { get; init; }

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

    public string? BeforeStateJson { get; init; }

    public string? AfterStateJson { get; init; }

    public string? MetadataJson { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
```

***

# 2. Validator

Buat folder kalau belum ada:

```bash
mkdir -p src/OrderManagement.Application/Validators/ActivityLogs
```

## `ActivityLogQueryDtoValidator.cs`

Create file:

```text
src/OrderManagement.Application/Validators/ActivityLogs/ActivityLogQueryDtoValidator.cs
```

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Application.Validators.ActivityLogs;

public sealed class ActivityLogQueryDtoValidator : AbstractValidator<ActivityLogQueryDto>
{
    public ActivityLogQueryDtoValidator()
    {
        RuleFor(query => query.CorrelationId)
            .MaximumLength(100)
            .WithMessage("Correlation id cannot be longer than 100 characters.")
            .When(query => !string.IsNullOrWhiteSpace(query.CorrelationId));

        RuleFor(query => query.OrderNumber)
            .MaximumLength(50)
            .WithMessage("Order number cannot be longer than 50 characters.")
            .When(query => !string.IsNullOrWhiteSpace(query.OrderNumber));

        RuleFor(query => query.ActivityType)
            .MaximumLength(100)
            .WithMessage("Activity type cannot be longer than 100 characters.")
            .When(query => !string.IsNullOrWhiteSpace(query.ActivityType));

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 200)
            .WithMessage("Page size must be between 1 and 200.");

        RuleFor(query => query)
            .Must(query =>
                query.FromDate is null ||
                query.ToDate is null ||
                query.FromDate <= query.ToDate)
            .WithMessage("From date must be less than or equal to to date.");
    }
}
```

***

# 3. Application Abstractions

***

## 3.1 `IActivityLogQueryRepository.cs`

Create file:

```text
src/OrderManagement.Application/Abstractions/ActivityLogs/IActivityLogQueryRepository.cs
```

```csharp
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogQueryRepository
{
    Task<PagedResult<ActivityLogListItemDto>> ListAsync(
        ActivityLogQueryDto query,
        CancellationToken cancellationToken = default);

    Task<ActivityLogDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
```

***

## 3.2 `IActivityLogQueryService.cs`

Create file:

```text
src/OrderManagement.Application/Abstractions/ActivityLogs/IActivityLogQueryService.cs
```

```csharp
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogQueryService
{
    Task<PagedResult<ActivityLogListItemDto>> ListAsync(
        ActivityLogQueryDto query,
        CancellationToken cancellationToken = default);

    Task<ActivityLogDetailDto> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
```

***

# 4. Application Service

## `ActivityLogQueryService.cs`

Create file:

```text
src/OrderManagement.Application/Services/ActivityLogQueryService.cs
```

```csharp
using FluentValidation;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Application.Services;

public sealed class ActivityLogQueryService : IActivityLogQueryService
{
    private readonly IActivityLogQueryRepository _repository;
    private readonly IValidator<ActivityLogQueryDto> _validator;

    public ActivityLogQueryService(
        IActivityLogQueryRepository repository,
        IValidator<ActivityLogQueryDto> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    public async Task<PagedResult<ActivityLogListItemDto>> ListAsync(
        ActivityLogQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new ActivityLogQueryDto
        {
            CorrelationId = string.IsNullOrWhiteSpace(query.CorrelationId)
                ? null
                : query.CorrelationId.Trim(),
            OrderId = query.OrderId,
            OrderNumber = string.IsNullOrWhiteSpace(query.OrderNumber)
                ? null
                : query.OrderNumber.Trim(),
            ActivityType = string.IsNullOrWhiteSpace(query.ActivityType)
                ? null
                : query.ActivityType.Trim(),
            ActorUserId = query.ActorUserId,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            Page = query.Page,
            PageSize = query.PageSize
        };

        var validationResult = await _validator.ValidateAsync(normalizedQuery, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Activity log query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        return await _repository.ListAsync(normalizedQuery, cancellationToken);
    }

    public async Task<ActivityLogDetailDto> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationAppException(
                "Activity log id validation failed.",
                [AppErrorDetail.ForField("id", "Activity log id is required.")]);
        }

        var log = await _repository.GetByIdAsync(id, cancellationToken);

        if (log is null)
        {
            throw new NotFoundAppException(
                "Activity log was not found.",
                "ACTIVITY_LOG_NOT_FOUND",
                [AppErrorDetail.ForField("id", "Activity log id does not exist.", new { id })]);
        }

        return log;
    }
}
```

***

# 5. Application DI Update

Update file:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.Validators.ActivityLogs;
```

Tambahkan registration:

```csharp
services.AddScoped<IActivityLogQueryService, ActivityLogQueryService>();
services.AddScoped<IValidator<ActivityLogQueryDto>, ActivityLogQueryDtoValidator>();
```

## Full final contoh

Kalau mau aman, replace dengan versi ini dan pastikan existing service lain tetap ada:

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Payments;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Application.Services;
using OrderManagement.Application.Validators.ActivityLogs;
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
        services.AddScoped<IActivityLogQueryService, ActivityLogQueryService>();

        services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
        services.AddScoped<IValidator<ProductListQueryDto>, ProductListQueryDtoValidator>();
        services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
        services.AddScoped<IValidator<ListOrdersQueryDto>, ListOrdersQueryValidator>();
        services.AddScoped<IValidator<UpdateOrderStatusCommand>, UpdateOrderStatusCommandValidator>();
        services.AddScoped<IValidator<CancelOrderCommand>, CancelOrderCommandValidator>();
        services.AddScoped<IValidator<CreatePaymentCommand>, CreatePaymentCommandValidator>();
        services.AddScoped<IValidator<ActivityLogQueryDto>, ActivityLogQueryDtoValidator>();

        services.AddSingleton<IOrderCancellationPolicy, OrderCancellationPolicy>();

        return services;
    }
}
```

***

# 6. Infrastructure Query Repository

## `ActivityLogQueryRepository.cs`

Create file:

```text
src/OrderManagement.Infrastructure/ActivityLogs/ActivityLogQueryRepository.cs
```

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class ActivityLogQueryRepository : IActivityLogQueryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ActivityLogQueryRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResult<ActivityLogListItemDto>> ListAsync(
        ActivityLogQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var offset = (query.Page - 1) * query.PageSize;

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            conditions.Add("correlation_id = @CorrelationId");
            parameters.Add("CorrelationId", query.CorrelationId);
        }

        if (query.OrderId is not null)
        {
            conditions.Add("order_id = @OrderId");
            parameters.Add("OrderId", query.OrderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.OrderNumber))
        {
            conditions.Add("order_number = @OrderNumber");
            parameters.Add("OrderNumber", query.OrderNumber);
        }

        if (!string.IsNullOrWhiteSpace(query.ActivityType))
        {
            conditions.Add("activity_type = @ActivityType");
            parameters.Add("ActivityType", query.ActivityType);
        }

        if (query.ActorUserId is not null)
        {
            conditions.Add("actor_user_id = @ActorUserId");
            parameters.Add("ActorUserId", query.ActorUserId.Value);
        }

        if (query.FromDate is not null)
        {
            conditions.Add("created_at >= @FromDate");
            parameters.Add("FromDate", query.FromDate.Value);
        }

        if (query.ToDate is not null)
        {
            conditions.Add("created_at <= @ToDate");
            parameters.Add("ToDate", query.ToDate.Value);
        }

        parameters.Add("PageSize", query.PageSize);
        parameters.Add("Offset", offset);

        var whereClause = conditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", conditions);

        var countSql = $"""
                        SELECT COUNT(*)
                        FROM activity_logs
                        {whereClause};
                        """;

        var dataSql = $"""
                       SELECT
                           id AS Id,
                           correlation_id AS CorrelationId,
                           activity_type AS ActivityType,
                           actor_user_id AS ActorUserId,
                           actor_username AS ActorUsername,
                           actor_role AS ActorRole,
                           order_id AS OrderId,
                           order_number AS OrderNumber,
                           product_id AS ProductId,
                           payment_id AS PaymentId,
                           request_path AS RequestPath,
                           http_method AS HttpMethod,
                           status_code AS StatusCode,
                           elapsed_ms AS ElapsedMs,
                           error_code AS ErrorCode,
                           metadata::text AS MetadataJson,
                           created_at AS CreatedAt
                       FROM activity_logs
                       {whereClause}
                       ORDER BY created_at DESC, id DESC
                       LIMIT @PageSize OFFSET @Offset;
                       """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                countSql,
                parameters,
                cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<ActivityLogListItemDto>(
            new CommandDefinition(
                dataSql,
                parameters,
                cancellationToken: cancellationToken));

        return new PagedResult<ActivityLogListItemDto>
        {
            Items = items.AsList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<ActivityLogDetailDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               correlation_id AS CorrelationId,
                               activity_type AS ActivityType,
                               actor_user_id AS ActorUserId,
                               actor_username AS ActorUsername,
                               actor_role AS ActorRole,
                               order_id AS OrderId,
                               order_number AS OrderNumber,
                               product_id AS ProductId,
                               payment_id AS PaymentId,
                               request_path AS RequestPath,
                               http_method AS HttpMethod,
                               status_code AS StatusCode,
                               elapsed_ms AS ElapsedMs,
                               error_code AS ErrorCode,
                               before_state::text AS BeforeStateJson,
                               after_state::text AS AfterStateJson,
                               metadata::text AS MetadataJson,
                               created_at AS CreatedAt
                           FROM activity_logs
                           WHERE id = @Id
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<ActivityLogDetailDto>(
            new CommandDefinition(
                sql,
                new { Id = id },
                cancellationToken: cancellationToken));
    }
}
```

Security:

```text
Tidak ada dynamic ORDER BY dari user.
Filter semua parameterized.
Pagination dibatasi validator.
```

***

# 7. Infrastructure DI Update

Update:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Tambahkan registration:

```csharp
services.AddScoped<IActivityLogQueryRepository, ActivityLogQueryRepository>();
```

Pastikan using sudah ada:

```csharp
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Infrastructure.ActivityLogs;
```

Bagian activity log registrations final:

```csharp
services.AddSingleton<IActivityLogQueue, BoundedChannelActivityLogQueue>();
services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
services.AddScoped<IActivityLogQueryRepository, ActivityLogQueryRepository>();
services.AddScoped<IActivityLogContextAccessor, HttpActivityLogContextAccessor>();
services.AddScoped<IActivityLogWriter, ActivityLogWriter>();
services.AddHostedService<ActivityLogBackgroundWorker>();
```

***

# 8. API Contracts

Buat folder:

```bash
mkdir -p src/OrderManagement.Api/Contracts/ActivityLogs
```

***

## 8.1 `ActivityLogQuery.cs`

Create file:

```text
src/OrderManagement.Api/Contracts/ActivityLogs/ActivityLogQuery.cs
```

```csharp
namespace OrderManagement.Api.Contracts.ActivityLogs;

public sealed class ActivityLogQuery
{
    public string? CorrelationId { get; init; }

    public Guid? OrderId { get; init; }

    public string? OrderNumber { get; init; }

    public string? ActivityType { get; init; }

    public Guid? ActorUserId { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}
```

***

## 8.2 `ActivityLogListItemResponse.cs`

Create file:

```text
src/OrderManagement.Api/Contracts/ActivityLogs/ActivityLogListItemResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.ActivityLogs;

public sealed class ActivityLogListItemResponse
{
    public Guid Id { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public string ActivityType { get; init; } = string.Empty;

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

    public string? MetadataJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
```

***

## 8.3 `ActivityLogDetailResponse.cs`

Create file:

```text
src/OrderManagement.Api/Contracts/ActivityLogs/ActivityLogDetailResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.ActivityLogs;

public sealed class ActivityLogDetailResponse
{
    public Guid Id { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    public string ActivityType { get; init; } = string.Empty;

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

    public string? BeforeStateJson { get; init; }

    public string? AfterStateJson { get; init; }

    public string? MetadataJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
```

***

# 9. Internal Activity Logs API Controller

Buat folder:

```bash
mkdir -p src/OrderManagement.Api/Controllers/Internal
```

## `InternalActivityLogsController.cs`

Create file:

```text
src/OrderManagement.Api/Controllers/Internal/InternalActivityLogsController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.ActivityLogs;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Api.Controllers.Internal;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOrOps)]
[Route("api/v1/internal/activity-logs")]
public sealed class InternalActivityLogsController : ControllerBase
{
    private readonly IActivityLogQueryService _service;

    public InternalActivityLogsController(IActivityLogQueryService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ActivityLogListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ActivityLogListItemResponse>>> List(
        [FromQuery] ActivityLogQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _service.ListAsync(
            new ActivityLogQueryDto
            {
                CorrelationId = query.CorrelationId,
                OrderId = query.OrderId,
                OrderNumber = query.OrderNumber,
                ActivityType = query.ActivityType,
                ActorUserId = query.ActorUserId,
                FromDate = query.FromDate,
                ToDate = query.ToDate,
                Page = query.Page,
                PageSize = query.PageSize
            },
            cancellationToken);

        return Ok(new PagedResponse<ActivityLogListItemResponse>
        {
            Items = result.Items
                .Select(MapListItem)
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

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ActivityLogDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ActivityLogDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);

        return Ok(MapDetail(result));
    }

    private static ActivityLogListItemResponse MapListItem(ActivityLogListItemDto item)
    {
        return new ActivityLogListItemResponse
        {
            Id = item.Id,
            CorrelationId = item.CorrelationId,
            ActivityType = item.ActivityType,
            ActorUserId = item.ActorUserId,
            ActorUsername = item.ActorUsername,
            ActorRole = item.ActorRole,
            OrderId = item.OrderId,
            OrderNumber = item.OrderNumber,
            ProductId = item.ProductId,
            PaymentId = item.PaymentId,
            RequestPath = item.RequestPath,
            HttpMethod = item.HttpMethod,
            StatusCode = item.StatusCode,
            ElapsedMs = item.ElapsedMs,
            ErrorCode = item.ErrorCode,
            MetadataJson = item.MetadataJson,
            CreatedAt = item.CreatedAt
        };
    }

    private static ActivityLogDetailResponse MapDetail(ActivityLogDetailDto item)
    {
        return new ActivityLogDetailResponse
        {
            Id = item.Id,
            CorrelationId = item.CorrelationId,
            ActivityType = item.ActivityType,
            ActorUserId = item.ActorUserId,
            ActorUsername = item.ActorUsername,
            ActorRole = item.ActorRole,
            OrderId = item.OrderId,
            OrderNumber = item.OrderNumber,
            ProductId = item.ProductId,
            PaymentId = item.PaymentId,
            RequestPath = item.RequestPath,
            HttpMethod = item.HttpMethod,
            StatusCode = item.StatusCode,
            ElapsedMs = item.ElapsedMs,
            ErrorCode = item.ErrorCode,
            BeforeStateJson = item.BeforeStateJson,
            AfterStateJson = item.AfterStateJson,
            MetadataJson = item.MetadataJson,
            CreatedAt = item.CreatedAt
        };
    }
}
```

***

# 10. Simple HTML Page

Kita bikin page internal sederhana di controller MVC style. Karena project Web API tetap bisa return `ContentResult` HTML.

## `InternalActivityLogsPageController.cs`

Create file:

```text
src/OrderManagement.Api/Controllers/Internal/InternalActivityLogsPageController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Extensions;

namespace OrderManagement.Api.Controllers.Internal;

[Authorize(Policy = AuthorizationPolicies.AdminOrOps)]
[Route("internal/activity-logs")]
public sealed class InternalActivityLogsPageController : Controller
{
    [HttpGet]
    public ContentResult Index()
    {
        const string html = """
                            <!doctype html>
                            <html lang="en">
                            <head>
                                <meta charset="utf-8">
                                <meta name="viewport" content="width=device-width, initial-scale=1">
                                <title>Activity Logs</title>
                                <style>
                                    :root {
                                        color-scheme: light;
                                        --bg: #f7f7fb;
                                        --card: #ffffff;
                                        --text: #171923;
                                        --muted: #64748b;
                                        --border: #e2e8f0;
                                        --primary: #2563eb;
                                        --danger: #dc2626;
                                        --success: #16a34a;
                                        --warning: #ca8a04;
                                    }
                                    * { box-sizing: border-box; }
                                    body {
                                        margin: 0;
                                        font-family: ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                                        background: var(--bg);
                                        color: var(--text);
                                    }
                                    header {
                                        padding: 24px;
                                        background: linear-gradient(135deg, #1d4ed8, #4f46e5);
                                        color: white;
                                    }
                                    header h1 {
                                        margin: 0;
                                        font-size: 24px;
                                    }
                                    header p {
                                        margin: 6px 0 0;
                                        opacity: .9;
                                    }
                                    main {
                                        padding: 24px;
                                        max-width: 1400px;
                                        margin: 0 auto;
                                    }
                                    .card {
                                        background: var(--card);
                                        border: 1px solid var(--border);
                                        border-radius: 16px;
                                        padding: 16px;
                                        box-shadow: 0 6px 18px rgba(15, 23, 42, .06);
                                    }
                                    .grid {
                                        display: grid;
                                        grid-template-columns: repeat(4, minmax(0, 1fr));
                                        gap: 12px;
                                    }
                                    label {
                                        display: block;
                                        font-size: 12px;
                                        font-weight: 600;
                                        color: var(--muted);
                                        margin-bottom: 4px;
                                    }
                                    input {
                                        width: 100%;
                                        padding: 10px 12px;
                                        border: 1px solid var(--border);
                                        border-radius: 10px;
                                        font-size: 14px;
                                    }
                                    button {
                                        border: none;
                                        border-radius: 10px;
                                        padding: 10px 14px;
                                        background: var(--primary);
                                        color: white;
                                        cursor: pointer;
                                        font-weight: 600;
                                    }
                                    button.secondary {
                                        background: #475569;
                                    }
                                    .actions {
                                        display: flex;
                                        gap: 8px;
                                        align-items: end;
                                    }
                                    table {
                                        width: 100%;
                                        border-collapse: collapse;
                                        margin-top: 16px;
                                        font-size: 13px;
                                    }
                                    th, td {
                                        padding: 10px;
                                        border-bottom: 1px solid var(--border);
                                        text-align: left;
                                        vertical-align: top;
                                    }
                                    th {
                                        color: var(--muted);
                                        font-size: 12px;
                                        text-transform: uppercase;
                                        letter-spacing: .04em;
                                    }
                                    tr:hover {
                                        background: #f8fafc;
                                    }
                                    .badge {
                                        display: inline-flex;
                                        align-items: center;
                                        padding: 4px 8px;
                                        border-radius: 999px;
                                        background: #e0e7ff;
                                        color: #3730a3;
                                        font-weight: 700;
                                        font-size: 12px;
                                    }
                                    .error {
                                        background: #fee2e2;
                                        color: #991b1b;
                                    }
                                    .success {
                                        background: #dcfce7;
                                        color: #166534;
                                    }
                                    .muted {
                                        color: var(--muted);
                                    }
                                    .mono {
                                        font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", monospace;
                                    }
                                    pre {
                                        white-space: pre-wrap;
                                        word-break: break-word;
                                        background: #0f172a;
                                        color: #e2e8f0;
                                        border-radius: 12px;
                                        padding: 12px;
                                        max-height: 300px;
                                        overflow: auto;
                                    }
                                    .details {
                                        margin-top: 16px;
                                    }
                                    .pagination {
                                        display: flex;
                                        gap: 8px;
                                        align-items: center;
                                        justify-content: flex-end;
                                        margin-top: 12px;
                                    }
                                    @media (max-width: 1000px) {
                                        .grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
                                    }
                                    @media (max-width: 640px) {
                                        .grid { grid-template-columns: 1fr; }
                                    }
                                </style>
                            </head>
                            <body>
                                <header>
                                    <h1>Activity Logs</h1>
                                    <p>Trace request and business activity by correlation ID, order ID, order number, or activity type.</p>
                                </header>

                                <main>
                                    <section class="card">
                                        <div class="grid">
                                            <div>
                                                <label>Correlation ID</label>
                                                <input id="correlationId" placeholder="log-create-order-001">
                                            </div>
                                            <div>
                                                <label>Order ID</label>
                                                <input id="orderId" placeholder="uuid">
                                            </div>
                                            <div>
                                                <label>Order Number</label>
                                                <input id="orderNumber" placeholder="ORD-20260617-000001">
                                            </div>
                                            <div>
                                                <label>Activity Type</label>
                                                <input id="activityType" placeholder="OrderCreated">
                                            </div>
                                            <div>
                                                <label>Actor User ID</label>
                                                <input id="actorUserId" placeholder="uuid">
                                            </div>
                                            <div>
                                                <label>From Date</label>
                                                <input id="fromDate" type="datetime-local">
                                            </div>
                                            <div>
                                                <label>To Date</label>
                                                <input id="toDate" type="datetime-local">
                                            </div>
                                            <div class="actions">
                                                <button onclick="search(1)">Search</button>
                                                <button class="secondary" onclick="resetFilters()">Reset</button>
                                            </div>
                                        </div>
                                    </section>

                                    <section class="card" style="margin-top: 16px;">
                                        <div id="summary" class="muted">No data loaded.</div>
                                        <table>
                                            <thead>
                                                <tr>
                                                    <th>Time</th>
                                                    <th>Activity</th>
                                                    <th>Correlation</th>
                                                    <th>Actor</th>
                                                    <th>Order</th>
                                                    <th>Status</th>
                                                    <th>Elapsed</th>
                                                    <th></th>
                                                </tr>
                                            </thead>
                                            <tbody id="rows"></tbody>
                                        </table>
                                        <div class="pagination">
                                            <button class="secondary" onclick="previousPage()">Prev</button>
                                            <span id="pageInfo" class="muted">Page -</span>
                                            <button class="secondary" onclick="nextPage()">Next</button>
                                        </div>
                                    </section>

                                    <section id="detail" class="card details" style="display:none;"></section>
                                </main>

                                <script>
                                    let currentPage = 1;
                                    let totalPages = 1;
                                    const pageSize = 50;

                                    function toIsoOrEmpty(value) {
                                        if (!value) return "";
                                        return new Date(value).toISOString();
                                    }

                                    function getQuery(page) {
                                        const params = new URLSearchParams();
                                        const fields = [
                                            "correlationId",
                                            "orderId",
                                            "orderNumber",
                                            "activityType",
                                            "actorUserId"
                                        ];

                                        for (const field of fields) {
                                            const value = document.getElementById(field).value.trim();
                                            if (value) params.set(field, value);
                                        }

                                        const fromDate = toIsoOrEmpty(document.getElementById("fromDate").value);
                                        const toDate = toIsoOrEmpty(document.getElementById("toDate").value);

                                        if (fromDate) params.set("fromDate", fromDate);
                                        if (toDate) params.set("toDate", toDate);

                                        params.set("page", page);
                                        params.set("pageSize", pageSize);

                                        return params.toString();
                                    }

                                    async function search(page) {
                                        currentPage = page;
                                        const response = await fetch(`/api/v1/internal/activity-logs?${getQuery(page)}`, {
                                            headers: {
                                                "Accept": "application/json"
                                            }
                                        });

                                        if (!response.ok) {
                                            const text = await response.text();
                                            alert(`Failed to load logs: ${response.status}\\n${text}`);
                                            return;
                                        }

                                        const data = await response.json();
                                        renderRows(data.items || []);
                                        renderPagination(data.pagination);
                                    }

                                    function renderRows(items) {
                                        const tbody = document.getElementById("rows");
                                        tbody.innerHTML = "";

                                        for (const item of items) {
                                            const tr = document.createElement("tr");
                                            const statusClass = item.errorCode
                                                ? "badge error"
                                                : item.statusCode >= 200 && item.statusCode < 400
                                                    ? "badge success"
                                                    : "badge";

                                            tr.innerHTML = `
                                                <td class="mono">${escapeHtml(formatDate(item.createdAt))}</td>
                                                <td><span class="badge">${escapeHtml(item.activityType)}</span>${item.errorCode ? `<div class="muted">${escapeHtml(item.errorCode)}</div>` : ""}</td>
                                                <td class="mono">${escapeHtml(item.correlationId)}</td>
                                                <td>${escapeHtml(item.actorUsername || "-")}<div class="muted">${escapeHtml(item.actorRole || "")}</div></td>
                                                <td>${escapeHtml(item.orderNumber || "-")}<div class="muted mono">${escapeHtml(item.orderId || "")}</div></td>
                                                <td>${item.statusCode ? `<span class="${statusClass}">${item.statusCode}</span>` : "-"}</td>
                                                <td>${item.elapsedMs != null ? `${item.elapsedMs} ms` : "-"}</td>
                                                <td><button onclick="loadDetail('${item.id}')">Detail</button></td>
                                            `;

                                            tbody.appendChild(tr);
                                        }

                                        document.getElementById("summary").textContent = `${items.length} item(s) loaded.`;
                                    }

                                    function renderPagination(pagination) {
                                        if (!pagination) return;
                                        currentPage = pagination.page;
                                        totalPages = pagination.totalPages;
                                        document.getElementById("pageInfo").textContent =
                                            `Page ${pagination.page} of ${pagination.totalPages} — Total ${pagination.totalItems}`;
                                    }

                                    async function loadDetail(id) {
                                        const response = await fetch(`/api/v1/internal/activity-logs/${id}`, {
                                            headers: {
                                                "Accept": "application/json"
                                            }
                                        });

                                        if (!response.ok) {
                                            const text = await response.text();
                                            alert(`Failed to load detail: ${response.status}\\n${text}`);
                                            return;
                                        }

                                        const item = await response.json();
                                        const detail = document.getElementById("detail");
                                        detail.style.display = "block";

                                        detail.innerHTML = `
                                            <h2>${escapeHtml(item.activityType)}</h2>
                                            <p class="muted mono">${escapeHtml(item.id)}</p>
                                            <div class="grid">
                                                <div><label>Correlation ID</label><div class="mono">${escapeHtml(item.correlationId)}</div></div>
                                                <div><label>Actor</label><div>${escapeHtml(item.actorUsername || "-")} (${escapeHtml(item.actorRole || "-")})</div></div>
                                                <div><label>Order</label><div>${escapeHtml(item.orderNumber || "-")}<br><span class="mono muted">${escapeHtml(item.orderId || "")}</span></div></div>
                                                <div><label>Request</label><div>${escapeHtml(item.httpMethod || "-")} ${escapeHtml(item.requestPath || "")}</div></div>
                                            </div>
                                            <h3>Before State</h3>
                                            <pre>${escapeHtml(prettyJson(item.beforeStateJson))}</pre>
                                            <h3>After State</h3>
                                            <pre>${escapeHtml(prettyJson(item.afterStateJson))}</pre>
                                            <h3>Metadata</h3>
                                            <pre>${escapeHtml(prettyJson(item.metadataJson))}</pre>
                                        `;

                                        detail.scrollIntoView({ behavior: "smooth", block: "start" });
                                    }

                                    function previousPage() {
                                        if (currentPage > 1) search(currentPage - 1);
                                    }

                                    function nextPage() {
                                        if (currentPage < totalPages) search(currentPage + 1);
                                    }

                                    function resetFilters() {
                                        for (const id of ["correlationId", "orderId", "orderNumber", "activityType", "actorUserId", "fromDate", "toDate"]) {
                                            document.getElementById(id).value = "";
                                        }
                                        search(1);
                                    }

                                    function formatDate(value) {
                                        if (!value) return "-";
                                        return new Date(value).toLocaleString();
                                    }

                                    function prettyJson(value) {
                                        if (!value) return "-";
                                        try {
                                            return JSON.stringify(JSON.parse(value), null, 2);
                                        } catch {
                                            return value;
                                        }
                                    }

                                    function escapeHtml(value) {
                                        return String(value ?? "")
                                            .replaceAll("&", "&amp;")
                                            .replaceAll("<", "&lt;")
                                            .replaceAll(">", "&gt;")
                                            .replaceAll('"', "&quot;")
                                            .replaceAll("'", "&#039;");
                                    }

                                    search(1);
                                </script>
                            </body>
                            </html>
                            """;

        return Content(html, "text/html; charset=utf-8");
    }
}
```

Security notes:

```text
- Page hanya untuk Admin/Ops.
- HTML escapes dynamic values.
- Tidak ada external JS/CSS.
- Data diambil dari protected API endpoint.
```

***

# 11. Optional: Hide Smoke Test Endpoint

Kalau sebelumnya di Batch 14A kita buat:

```text
InternalActivityLogsTestController
```

Boleh tetap ada sebagai internal diagnostics. Tapi untuk final submission lebih clean kalau route-nya jelas diagnostics:

```text
/api/v1/internal/activity-logs/test
```

Karena sudah protected Admin/Ops, aman untuk POC.

Kalau mau hapus juga boleh.

***

# 12. Program.cs Requirement

Pastikan controller MVC tetap jalan. Karena kita return `ContentResult`, tidak perlu Razor/MVC views. `builder.Services.AddControllers()` sudah cukup.

***

# 13. Build

Run:

```bash
dotnet build
```

Kalau ada error missing registration, cek:

```text
IActivityLogQueryService registered in Application
IActivityLogQueryRepository registered in Infrastructure
ActivityLogQueryDtoValidator registered
```

***

# 14. Run API

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

***

# 15. Manual Test API

Login admin:

```bash
ADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Password123!"}')

ADMIN_TOKEN=$(echo "$ADMIN_LOGIN" | jq -r '.accessToken')
```

List logs:

```bash
curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?page=1&pageSize=20" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  | jq
```

Filter correlation:

```bash
curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?correlationId=log-create-order-001&page=1&pageSize=20" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  | jq
```

Get detail:

```bash
LOG_ID=$(curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?page=1&pageSize=1" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  | jq -r '.items[0].id')

curl -k -s "https://localhost:7000/api/v1/internal/activity-logs/$LOG_ID" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  | jq
```

***

# 16. Manual Test Page

Open browser:

```text
https://localhost:7000/internal/activity-logs
```

Login/auth issue:

Karena HTML page fetch API memakai browser cookie? Kita pakai JWT bearer, dan browser page belum punya token injection. Ada 2 opsi:

## Opsi A — Pakai Swagger/API only

API sudah cukup untuk tracing.

## Opsi B — Simple token input di page

Supaya page bisa dipakai di browser tanpa cookie auth, kita perlu input token dan fetch pakai bearer token.

Karena endpoint page sendiri juga `[Authorize]`, browser direct open akan butuh bearer token dan biasanya tidak bisa set header dari address bar.

Untuk demo internal page, ada dua pendekatan:

### Pendekatan 1: Page endpoint `[AllowAnonymous]`, API tetap protected

Page HTML bisa terbuka, tapi data API tetap butuh token input.

Ini aman karena page static tidak expose data tanpa token.

### Pendekatan 2: Pakai cookie auth

Lebih besar scope, tidak kita lakukan.

## Rekomendasi POC

Ubah page controller jadi:

```csharp
[AllowAnonymous]
[Route("internal/activity-logs")]
public sealed class InternalActivityLogsPageController : Controller
```

Lalu di HTML tambahkan input token dan fetch Authorization header.

Karena lu minta page buat demo, gue sarankan pakai **AllowAnonymous untuk HTML shell saja**, sementara API data tetap Admin/Ops JWT protected.

***

# 17. Update HTML Page untuk Token Input

Kalau pakai rekomendasi di atas, update controller attribute:

```csharp
[AllowAnonymous]
[Route("internal/activity-logs")]
public sealed class InternalActivityLogsPageController : Controller
```

Tambahkan di form grid HTML field:



Lalu ubah fetch di JS:

```javascript
function authHeaders() {
    const token = document.getElementById("token").value.trim();
    const headers = {
        "Accept": "application/json"
    };

    if (token) {
        headers["Authorization"] = `Bearer ${token}`;
    }

    return headers;
}
```

Ganti fetch list:

```javascript
const response = await fetch(`/api/v1/internal/activity-logs?${getQuery(page)}`, {
    headers: authHeaders()
});
```

Ganti fetch detail:

```javascript
const response = await fetch(`/api/v1/internal/activity-logs/${id}`, {
    headers: authHeaders()
});
```

Dengan ini:

```text
/internal/activity-logs page bisa dibuka.
Data tetap tidak keluar tanpa JWT Admin/Ops.
```

Security:

```text
Token hanya disimpan di memory DOM input, tidak localStorage.
Refresh page akan hilang.
```

***

# 18. Acceptance Criteria

Harus sukses:

```text
- Admin/Ops bisa hit GET /api/v1/internal/activity-logs.
- Customer tidak bisa hit internal logs API.
- Anonymous tidak bisa hit internal logs API.
- Filter correlationId bekerja.
- Filter orderId/orderNumber/activityType bekerja.
- Pagination bounded.
- Detail endpoint menampilkan before/after/metadata.
- Page bisa search timeline dengan token Admin/Ops.
```

Tidak boleh terjadi:

```text
- Customer melihat logs.
- Anonymous API melihat logs.
- SQL injection lewat filter.
- Page render raw HTML dari metadata tanpa escaping.
- Token disimpan localStorage/sessionStorage.
```

***

# 19. Security Test

Customer token:

```bash
CUSTOMER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"customer1","password":"Password123!"}')

CUSTOMER_TOKEN=$(echo "$CUSTOMER_LOGIN" | jq -r '.accessToken')
```

Try access logs:

```bash
curl -k -i "https://localhost:7000/api/v1/internal/activity-logs?page=1&pageSize=20" \
  -H "Authorization: Bearer $CUSTOMER_TOKEN"
```

Expected:

```text
403 FORBIDDEN
```

***

# 20. Commit Batch 14C

```bash
git add .
git commit -m "feat: add internal activity log API and tracing page"
```

***