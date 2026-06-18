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