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