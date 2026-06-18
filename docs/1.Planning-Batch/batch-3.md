Siap bro, lanjut **Batch 3: Global Exception Handling, Correlation ID, Logging Middleware**.

Target batch ini:

```text
1. Standard API error response contract
2. Correlation ID middleware
3. Global exception handling middleware
4. Request logging middleware
5. Middleware registration extension
6. Program.cs pipeline update
```

Setelah Batch 3, semua error dari `Application.Exceptions.AppException` akan keluar dalam format JSON konsisten, dan semua request punya `X-Correlation-ID`.

***

# Batch 3 — Global Exception, Correlation ID, Logging Middleware

## 1. API Common Contracts

***

## 1.1 `ApiErrorDetail.cs`

Replace file:

```text
src/OrderManagement.Api/Contracts/Common/ApiErrorDetail.cs
```

Isi:

```csharp
namespace OrderManagement.Api.Contracts.Common;

public sealed class ApiErrorDetail
{
    public ApiErrorDetail()
    {
    }

    public ApiErrorDetail(string? field, string message, object? metadata = null)
    {
        Field = field;
        Message = message;
        Metadata = metadata;
    }

    public string? Field { get; init; }

    public string Message { get; init; } = string.Empty;

    public object? Metadata { get; init; }
}
```

***

## 1.2 `ApiError.cs`

Replace file:

```text
src/OrderManagement.Api/Contracts/Common/ApiError.cs
```

Isi:

```csharp
namespace OrderManagement.Api.Contracts.Common;

public sealed class ApiError
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyCollection<ApiErrorDetail> Details { get; init; } = [];

    public string CorrelationId { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }
}
```

***

## 1.3 `ApiErrorResponse.cs`

Replace file:

```text
src/OrderManagement.Api/Contracts/Common/ApiErrorResponse.cs
```

Isi:

```csharp
namespace OrderManagement.Api.Contracts.Common;

public sealed class ApiErrorResponse
{
    public ApiError Error { get; init; } = new();
}
```

***

## 1.4 `PaginationResponse.cs`

Replace file:

```text
src/OrderManagement.Api/Contracts/Common/PaginationResponse.cs
```

Isi:

```csharp
namespace OrderManagement.Api.Contracts.Common;

public sealed class PaginationResponse
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public long TotalItems { get; init; }

    public int TotalPages { get; init; }
}
```

***

## 1.5 `PagedResponse.cs`

Replace file:

```text
src/OrderManagement.Api/Contracts/Common/PagedResponse.cs
```

Isi:

```csharp
namespace OrderManagement.Api.Contracts.Common;

public sealed class PagedResponse<T>
{
    public IReadOnlyCollection<T> Items { get; init; } = [];

    public PaginationResponse Pagination { get; init; } = new();
}
```

***

# 2. Correlation ID Constants

Create file baru:

```text
src/OrderManagement.Api/Middleware/CorrelationIdConstants.cs
```

Isi:

```csharp
namespace OrderManagement.Api.Middleware;

public static class CorrelationIdConstants
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogPropertyName = "CorrelationId";
    public const string HttpContextItemName = "CorrelationId";
}
```

***

# 3. Correlation ID Middleware

Replace file:

```text
src/OrderManagement.Api/Middleware/CorrelationIdMiddleware.cs
```

Isi:

```csharp
using Serilog.Context;

namespace OrderManagement.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        context.Items[CorrelationIdConstants.HttpContextItemName] = correlationId;
        context.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;

        using (LogContext.PushProperty(CorrelationIdConstants.LogPropertyName, correlationId))
        {
            _logger.LogDebug(
                "Correlation ID {CorrelationId} assigned to request {Method} {Path}",
                correlationId,
                context.Request.Method,
                context.Request.Path.Value);

            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdConstants.HeaderName, out var values))
        {
            var value = values.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
```

***

# 4. Global Exception Handling Middleware

Replace file:

```text
src/OrderManagement.Api/Middleware/GlobalExceptionHandlingMiddleware.cs
```

Isi:

```csharp
using System.Net.Mime;
using System.Text.Json;
using Npgsql;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Application.Constants;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Api.Middleware;

public sealed class GlobalExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = GetCorrelationId(context);
        var timestamp = DateTimeOffset.UtcNow;

        var errorResponse = exception switch
        {
            AppException appException => BuildFromAppException(appException, correlationId, timestamp),
            PostgresException postgresException => BuildFromPostgresException(postgresException, correlationId, timestamp),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested =>
                BuildClientCancelledResponse(correlationId, timestamp),
            _ => BuildUnhandledExceptionResponse(correlationId, timestamp)
        };

        var statusCode = exception switch
        {
            AppException appException => appException.StatusCode,
            PostgresException => StatusCodes.Status409Conflict,
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => 499,
            _ => StatusCodes.Status500InternalServerError
        };

        LogException(context, exception, statusCode, correlationId, errorResponse.Error.Code);

        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Cannot write error response because response has already started. CorrelationId={CorrelationId}",
                correlationId);

            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        context.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;

        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            errorResponse,
            JsonSerializerOptions,
            context.RequestAborted);
    }

    private static ApiErrorResponse BuildFromAppException(
        AppException exception,
        string correlationId,
        DateTimeOffset timestamp)
    {
        return new ApiErrorResponse
        {
            Error = new ApiError
            {
                Code = exception.Code,
                Message = exception.Message,
                Details = exception.Details
                    .Select(detail => new ApiErrorDetail(detail.Field, detail.Message, detail.Metadata))
                    .ToArray(),
                CorrelationId = correlationId,
                Timestamp = timestamp
            }
        };
    }

    private static ApiErrorResponse BuildFromPostgresException(
        PostgresException exception,
        string correlationId,
        DateTimeOffset timestamp)
    {
        var code = ErrorCodes.DatabaseConstraintViolation;
        var message = "Database constraint violation occurred.";

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            message = "Duplicate data violates a unique database constraint.";
        }
        else if (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            message = "Referenced data does not exist.";
        }
        else if (exception.SqlState == PostgresErrorCodes.CheckViolation)
        {
            message = "Data violates a database check constraint.";
        }

        return new ApiErrorResponse
        {
            Error = new ApiError
            {
                Code = code,
                Message = message,
                Details =
                [
                    new ApiErrorDetail(
                        null,
                        "Database rejected the operation.",
                        new
                        {
                            constraint = exception.ConstraintName,
                            table = exception.TableName,
                            sqlState = exception.SqlState
                        })
                ],
                CorrelationId = correlationId,
                Timestamp = timestamp
            }
        };
    }

    private static ApiErrorResponse BuildClientCancelledResponse(
        string correlationId,
        DateTimeOffset timestamp)
    {
        return new ApiErrorResponse
        {
            Error = new ApiError
            {
                Code = "CLIENT_CLOSED_REQUEST",
                Message = "The client closed the request before the operation completed.",
                Details = [],
                CorrelationId = correlationId,
                Timestamp = timestamp
            }
        };
    }

    private static ApiErrorResponse BuildUnhandledExceptionResponse(
        string correlationId,
        DateTimeOffset timestamp)
    {
        return new ApiErrorResponse
        {
            Error = new ApiError
            {
                Code = ErrorCodes.InternalServerError,
                Message = "An unexpected error occurred.",
                Details = [],
                CorrelationId = correlationId,
                Timestamp = timestamp
            }
        };
    }

    private void LogException(
        HttpContext context,
        Exception exception,
        int statusCode,
        string correlationId,
        string errorCode)
    {
        if (statusCode >= 500)
        {
            _logger.LogError(
                exception,
                "Unhandled exception occurred. ErrorCode={ErrorCode} StatusCode={StatusCode} CorrelationId={CorrelationId} Method={Method} Path={Path}",
                errorCode,
                statusCode,
                correlationId,
                context.Request.Method,
                context.Request.Path.Value);

            return;
        }

        _logger.LogWarning(
            exception,
            "Handled exception occurred. ErrorCode={ErrorCode} StatusCode={StatusCode} CorrelationId={CorrelationId} Method={Method} Path={Path}",
            errorCode,
            statusCode,
            correlationId,
            context.Request.Method,
            context.Request.Path.Value);
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(CorrelationIdConstants.HttpContextItemName, out var value) &&
            value is string correlationId &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }

        if (context.Request.Headers.TryGetValue(CorrelationIdConstants.HeaderName, out var values))
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
```

***

# 5. Request Logging Middleware

Replace file:

```text
src/OrderManagement.Api/Middleware/RequestLoggingMiddleware.cs
```

Isi:

```csharp
using System.Diagnostics;
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
            }
        }
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

***

# 6. Middleware Extension

Replace file:

```text
src/OrderManagement.Api/Extensions/MiddlewareExtensions.cs
```

Isi:

```csharp
using OrderManagement.Api.Middleware;

namespace OrderManagement.Api.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseApiMiddlewares(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();

        return app;
    }
}
```

***

# 7. Update `Program.cs`

Replace file:

```text
src/OrderManagement.Api/Program.cs
```

Dengan versi ini:

```csharp
using OrderManagement.Api.Extensions;
using OrderManagement.Api.Options;
using OrderManagement.Application;
using OrderManagement.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.Configure<ClientCorsOptions>(
    builder.Configuration.GetSection(ClientCorsOptions.SectionName));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    var corsOptions = builder.Configuration
        .GetSection(ClientCorsOptions.SectionName)
        .Get<ClientCorsOptions>() ?? new ClientCorsOptions();

    options.AddPolicy("ClientApps", policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsOptions.AllowedOrigins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy
            .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-ID")
            .WithExposedHeaders("X-Correlation-ID", "Location");
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

await app.ApplyDatabaseMigrationsAsync();

app.UseApiMiddlewares();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
```

***

# 8. Optional: Test Controller Sementara

Kalau lu mau ngetes middleware tanpa nunggu Batch Auth/Product, boleh buat controller debug sementara. Tapi ini **opsional** dan nanti bisa dihapus.

Create:

```text
src/OrderManagement.Api/Controllers/DiagnosticsController.cs
```

Isi:

```csharp
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Application.Constants;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/v1/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    [HttpGet("ok")]
    public IActionResult OkResponse()
    {
        return Ok(new
        {
            message = "OK",
            correlationId = HttpContext.Response.Headers["X-Correlation-ID"].ToString()
        });
    }

    [HttpGet("app-error")]
    public IActionResult AppError()
    {
        throw new BusinessRuleAppException(
            ErrorCodes.InvalidOrderStatusTransition,
            "Diagnostic business rule exception.");
    }

    [HttpGet("unhandled-error")]
    public IActionResult UnhandledError()
    {
        throw new InvalidOperationException("Diagnostic unhandled exception.");
    }
}
```

Test:

```bash
curl -i http://localhost:5000/api/v1/diagnostics/ok
```

```bash
curl -i http://localhost:5000/api/v1/diagnostics/app-error
```

```bash
curl -i http://localhost:5000/api/v1/diagnostics/unhandled-error
```

Expected:

* Response selalu punya header `X-Correlation-ID`.
* Error response format konsisten.
* Unhandled error tidak bocorin stack trace.

Kalau tidak mau ada endpoint debug di repo final, skip file ini atau hapus sebelum submission.

***

# 9. Build & Run

Run:

```bash
dotnet build
```

Lalu:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

Kalau PostgreSQL belum siap, startup akan gagal di migration runner dari Batch 1. Itu normal karena API memang auto-check schema saat startup.

***

# 10. Expected Error Response Example

Kalau pakai diagnostics:

```json
{
  "error": {
    "code": "INVALID_ORDER_STATUS_TRANSITION",
    "message": "Diagnostic business rule exception.",
    "details": [],
    "correlationId": "6e704a59f9f747e5a40595a155f5dc79",
    "timestamp": "2026-06-17T05:40:00.0000000+00:00"
  }
}
```

Unhandled exception:

```json
{
  "error": {
    "code": "INTERNAL_SERVER_ERROR",
    "message": "An unexpected error occurred.",
    "details": [],
    "correlationId": "6e704a59f9f747e5a40595a155f5dc79",
    "timestamp": "2026-06-17T05:40:00.0000000+00:00"
  }
}
```

***

# 11. Important Note: Middleware Order

Urutan ini penting:

```csharp
app.UseApiMiddlewares();

app.UseSwagger();
app.UseSwaggerUI();

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.UseCors("ClientApps");

app.UseAuthorization();

app.MapControllers();
```

Kenapa `CorrelationIdMiddleware` duluan?

Karena semua middleware dan error setelahnya butuh correlation ID.

Kenapa `GlobalExceptionHandlingMiddleware` sebelum `RequestLoggingMiddleware`?

Supaya exception dari request pipeline bisa diubah jadi response JSON konsisten.

***

# 12. Commit Batch 3

```bash
git add .
git commit -m "feat: add global exception handling and correlation logging middleware"
```

***

# 13. Next Batch

Next kita masuk:

```text
Batch 4: Auth/Login JWT + User Repository + Password Hashing
```

Di Batch 4 kita akan implement:

```text
ICurrentUserContext
IJwtTokenGenerator
IPasswordHasher
IUserRepository
AuthService
Login DTOs
Login validator
JWT generator
BCrypt password hasher
CurrentUserContext
UserRepository with Dapper
AuthController
AuthenticationExtensions
AuthorizationExtensions
Program.cs auth update
```

Setelah Batch 4, endpoint ini akan jalan:

```http
POST /api/v1/auth/login
```

Lalu mulai bisa dipakai untuk endpoint berikutnya.
