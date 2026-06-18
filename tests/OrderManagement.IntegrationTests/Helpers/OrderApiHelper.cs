using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Helpers;

public static class OrderApiHelper
{
    public static async Task<Guid> CreateOrderAsync(
        HttpClient client,
        string token,
        Guid customerId,
        Guid productId,
        int quantity,
        string? idempotencyKey = null)
    {
        using var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Post,
            "/api/v1/orders",
            token,
            new
            {
                customerId,
                items = new[]
                {
                    new
                    {
                        productId,
                        quantity
                    }
                },
                shippingAddress = "Jl. Integration Test"
            },
            idempotencyKey ?? Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var json = await response.ReadJsonAsync();

        return json.RootElement.GetProperty("id").GetGuid();
    }

    public static async Task<HttpResponseMessage> CreateOrderRawAsync(
        HttpClient client,
        string token,
        Guid customerId,
        Guid productId,
        int quantity,
        string idempotencyKey)
    {
        var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Post,
            "/api/v1/orders",
            token,
            new
            {
                customerId,
                items = new[]
                {
                    new
                    {
                        productId,
                        quantity
                    }
                },
                shippingAddress = "Jl. Integration Test"
            },
            idempotencyKey);

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PayAsync(
        HttpClient client,
        string token,
        Guid orderId,
        string simulateResult = "Success")
    {
        using var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/orders/{orderId}/payments",
            token,
            new
            {
                provider = "MockPayment",
                simulateResult
            });

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> CancelAsync(
        HttpClient client,
        string token,
        Guid orderId,
        long expectedRowVersion,
        string cancellationReason = "CustomerRequested")
    {
        using var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v1/orders/{orderId}/cancel",
            token,
            new
            {
                expectedRowVersion,
                cancellationReason,
                reason = "Integration test cancel."
            });

        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> UpdateStatusAsync(
        HttpClient client,
        string token,
        Guid orderId,
        long expectedRowVersion,
        string targetStatus)
    {
        using var request = HttpJsonHelper.CreateJsonRequest(
            HttpMethod.Patch,
            $"/api/v1/orders/{orderId}/status",
            token,
            new
            {
                targetStatus,
                expectedRowVersion,
                reason = "Integration test update."
            });

        return await client.SendAsync(request);
    }
}