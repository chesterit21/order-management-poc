using System.Net.Mime;
using System.Text.Json;
using Npgsql;
using Microsoft.Extensions.Logging;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Application.Constants;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;

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
        var statusCode = GetStatusCode(exception);
        var errorResponse = BuildErrorResponse(exception, correlationId, timestamp);

        LogException(context, exception, statusCode, correlationId, errorResponse.Error.Code);
        TryWriteRequestFailedActivity(context, statusCode, correlationId, errorResponse.Error.Code, exception);

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

    private static int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            AppException appException => appException.StatusCode,
            PostgresException => StatusCodes.Status409Conflict,
            OperationCanceledException => 499,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private static ApiErrorResponse BuildErrorResponse(Exception exception, string correlationId, DateTimeOffset timestamp) =>
        exception switch
        {
            AppException appException => BuildFromAppException(appException, correlationId, timestamp),
            PostgresException postgresException => BuildFromPostgresException(postgresException, correlationId, timestamp),
            OperationCanceledException => BuildClientCancelledResponse(correlationId, timestamp),
            _ => BuildUnhandledExceptionResponse(correlationId, timestamp)
        };

    private void TryWriteRequestFailedActivity(
        HttpContext context,
        int statusCode,
        string correlationId,
        string errorCode,
        Exception exception)
    {
        var writer = context.RequestServices.GetService<IActivityLogWriter>();

        writer?.TryWrite(
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

        // SECURITY: Internal database details (constraint name, table name, SQL state) are
        // NOT exposed to the client. They are logged server-side only (via the LogException
        // method which includes the full exception object) for ops debugging. The client
        // receives only a generic message + the correlation ID so they can reference it
        // when contacting support.
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
                        "The operation was rejected by the data store. Refer to the correlation ID when contacting support.",
                        null)
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