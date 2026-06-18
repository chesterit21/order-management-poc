using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OrderManagement.Application.Abstractions.ActivityLogs;

namespace OrderManagement.Api.Extensions.ActivityLogs;

public sealed class HttpActivityLogContextAccessor(IHttpContextAccessor httpContextAccessor) : IActivityLogContextAccessor
{
    private const string CorrelationIdItemName = "CorrelationId";
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

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