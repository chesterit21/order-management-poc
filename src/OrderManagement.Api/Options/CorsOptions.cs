namespace OrderManagement.Api.Options;

public sealed class ClientCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = [];
}