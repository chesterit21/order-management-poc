using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using OrderManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace OrderManagement.IntegrationTests.Api;

public sealed class DemoApiTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public DemoApiTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateOrder_ViaDemoEndpoint_ShouldReturnCreated_WithoutAuth()
    {
        // Arrange
        await _factory.ResetDatabaseAsync();
        var client = _factory.Client;

        var request = new
        {
            requestedByUserId = TestUsers.Buyer1Id,
            requestedByRole = "Buyer",
            customerId = TestUsers.Buyer1Id,
            items = new[]
            {
                new { productId = TestProducts.MouseId, quantity = 1 }
            },
            shippingAddress = "Demo Address 123"
        };

        var requestMessage = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/v1/demo/orders");
        requestMessage.Content = JsonContent.Create(request);
        requestMessage.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Act
        // No Auth Token added!
        var response = await client.SendAsync(requestMessage);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
    
    [Fact]
    public async Task UpdateStatus_ViaDemoEndpoint_ShouldReturnOk_WithoutAuth()
    {
        // Arrange
        await _factory.ResetDatabaseAsync();
        var client = _factory.Client;
        
        // 1. Create order first via Demo
        var createRequest = new
        {
            requestedByUserId = TestUsers.Buyer1Id,
            requestedByRole = "Buyer",
            customerId = TestUsers.Buyer1Id,
            items = new[]
            {
                new { productId = TestProducts.MouseId, quantity = 1 }
            },
            shippingAddress = "Demo Address 123"
        };
        var createMsg = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/v1/demo/orders");
        createMsg.Content = JsonContent.Create(createRequest);
        createMsg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var createResponse = await client.SendAsync(createMsg);
        
        var orderResponse = await createResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var orderId = orderResponse.GetProperty("id").GetGuid();
        var rowVersion = orderResponse.GetProperty("rowVersion").GetInt64();

        // 2. Update status via Demo (simulate admin)
        var updateRequest = new
        {
            requestedByUserId = TestUsers.ApplicationAdminId,
            requestedByRole = "ApplicationAdmin",
            targetStatus = "Confirmed",
            expectedRowVersion = rowVersion,
            reason = "Demo confirmation"
        };

        // Act
        var response = await client.PatchAsJsonAsync($"/api/v1/demo/orders/{orderId}/status", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
