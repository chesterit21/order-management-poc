using OrderManagement.Api.Middleware;

namespace OrderManagement.Api.Extensions;

public static class MiddlewareExtensions
{
/// <summary>
/// Registers the API middleware pipeline in the correct order.
///
/// Order (outermost → innermost):
///   1. CorrelationIdMiddleware  — assigns/propagates correlation ID (must be first so all
///                                 downstream middleware and logs have access to it).
///   2. RequestLoggingMiddleware — wraps the entire request lifecycle with a try/finally to
///                                 log "Request started/completed" and write the
///                                 RequestCompleted activity log entry. By placing this
///                                 OUTSIDE the exception handler, the finally block runs
///                                 AFTER GlobalExceptionHandlingMiddleware has set the final
///                                 HTTP status code, so the logged status code is accurate.
///   3. GlobalExceptionHandlingMiddleware — catches unhandled exceptions, maps them to the
///                                 standard ApiErrorResponse, sets the correct status code,
///                                 and writes the RequestFailed activity log entry.
///
/// Previous (buggy) order was: CorrelationId → GlobalException → RequestLogging.
/// That caused RequestLogging's finally block to execute BEFORE the exception handler
/// set the status code, so every failed request was logged with status 200.
/// </summary>
public static IApplicationBuilder UseApiMiddlewares(this IApplicationBuilder app)
{
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

    return app;
}
}