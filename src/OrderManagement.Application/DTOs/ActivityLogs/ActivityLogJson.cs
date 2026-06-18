using System.Text.Json;

namespace OrderManagement.Application.DTOs.ActivityLogs;

public static class ActivityLogJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static string? SerializeOrNull(object? value)
    {
        return value is null
            ? null
            : JsonSerializer.Serialize(value, JsonOptions);
    }
}