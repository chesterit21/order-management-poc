using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Middleware;
using OrderManagement.Application.Constants;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Api.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireIdempotencyKeyFilter : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<IdempotencyOptions>>()
            .Value;

        var headerName = string.IsNullOrWhiteSpace(options.HeaderName)
            ? "Idempotency-Key"
            : options.HeaderName;

        if (!context.HttpContext.Request.Headers.TryGetValue(headerName, out var values) ||
            string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            context.Result = BuildErrorResult(
                context,
                StatusCodes.Status400BadRequest,
                ErrorCodes.IdempotencyKeyRequired,
                $"{headerName} header is required.");

            return;
        }

        var key = values.FirstOrDefault()!.Trim();

        if (key.Length > options.KeyMaxLength)
        {
            context.Result = BuildErrorResult(
                context,
                StatusCodes.Status422UnprocessableEntity,
                ErrorCodes.ValidationError,
                $"{headerName} header cannot be longer than {options.KeyMaxLength} characters.",
                new ApiErrorDetail(
                    headerName,
                    "Idempotency key is too long.",
                    new
                    {
                        maxLength = options.KeyMaxLength
                    }));

            return;
        }

        await next();
    }

    private static ObjectResult BuildErrorResult(
        ActionExecutingContext context,
        int statusCode,
        string code,
        string message,
        ApiErrorDetail? detail = null)
    {
        var correlationId = GetCorrelationId(context.HttpContext);

        var response = new ApiErrorResponse
        {
            Error = new ApiError
            {
                Code = code,
                Message = message,
                Details = detail is null ? [] : [detail],
                CorrelationId = correlationId,
                Timestamp = DateTimeOffset.UtcNow
            }
        };

        context.HttpContext.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;

        return new ObjectResult(response)
        {
            StatusCode = statusCode
        };
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