namespace OrderManagement.Api.Extensions;

public static class HttpRequestEndpointExtensions
{
    public static string GetNormalizedEndpoint(this HttpRequest request)
    {
        var method = request.Method.ToUpperInvariant();
        var path = request.Path.Value?.Trim().ToLowerInvariant() ?? string.Empty;

        return $"{method} {path}";
    }
}