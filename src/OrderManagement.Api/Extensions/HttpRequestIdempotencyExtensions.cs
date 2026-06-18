using Microsoft.Extensions.Options;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Api.Extensions;

public static class HttpRequestIdempotencyExtensions
{
    public static string GetRequiredIdempotencyKey(this HttpRequest request)
    {
        var options = request.HttpContext.RequestServices
            .GetRequiredService<IOptions<IdempotencyOptions>>()
            .Value;

        var headerName = string.IsNullOrWhiteSpace(options.HeaderName)
            ? "Idempotency-Key"
            : options.HeaderName;

        if (!request.Headers.TryGetValue(headerName, out var values))
        {
            throw new InvalidOperationException($"{headerName} header was not found.");
        }

        var key = values.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"{headerName} header was empty.");
        }

        return key.Trim();
    }
}