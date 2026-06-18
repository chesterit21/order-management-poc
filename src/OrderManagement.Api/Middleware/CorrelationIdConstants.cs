namespace OrderManagement.Api.Middleware;

public static class CorrelationIdConstants
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogPropertyName = "CorrelationId";
    public const string HttpContextItemName = "CorrelationId";
}