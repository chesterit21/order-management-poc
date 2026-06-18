using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderManagement.IntegrationTests.Helpers;

public static class HttpJsonHelper
{
    public static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string url,
        string token,
        object body,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(json);
    }
}