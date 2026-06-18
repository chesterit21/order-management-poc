using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OrderManagement.Application.Abstractions.Idempotency;

namespace OrderManagement.Infrastructure.Idempotency;

public sealed class RequestHashService : IRequestHashService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public string ComputeHash<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = JsonSerializer.Serialize(request, SerializerOptions);

        return ComputeHashFromJson(json);
    }

    public string ComputeHashFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON payload is required.", nameof(json));
        }

        var normalizedJson = NormalizeJson(json);
        var bytes = Encoding.UTF8.GetBytes(normalizedJson);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeJson(string json)
    {
        var node = JsonNode.Parse(json)
            ?? throw new ArgumentException("JSON payload is invalid.", nameof(json));

        var normalizedNode = NormalizeNode(node);

        return normalizedNode.ToJsonString(SerializerOptions);
    }

    private static JsonNode NormalizeNode(JsonNode node)
    {
        return node switch
        {
            JsonObject jsonObject => NormalizeObject(jsonObject),
            JsonArray jsonArray => NormalizeArray(jsonArray),
            JsonValue jsonValue => jsonValue.DeepClone(),
            _ => node.DeepClone()
        };
    }

    private static JsonObject NormalizeObject(JsonObject jsonObject)
    {
        var normalized = new JsonObject();

        foreach (var property in jsonObject.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            normalized[property.Key] = property.Value is null
                ? null
                : NormalizeNode(property.Value);
        }

        return normalized;
    }

    private static JsonArray NormalizeArray(JsonArray jsonArray)
    {
        var normalized = new JsonArray();

        foreach (var item in jsonArray)
        {
            normalized.Add(item is null ? null : NormalizeNode(item));
        }

        return normalized;
    }
}