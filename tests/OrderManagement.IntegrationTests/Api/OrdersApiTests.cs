using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace OrderManagement.IntegrationTests.Api;

public sealed class OrdersApiTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public OrdersApiTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateOrder_WithValidRequest_ShouldReturnCreated()
    {
        // Arrange
        await _factory.ResetDatabaseAsync();
        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "buyer1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var request = new CreateOrderRequest
        {
            CustomerId = TestUsers.Buyer1Id,
            ShippingAddress = "123 Main St",
            Items = new[]
            {
                new CreateOrderItemRequest { ProductId = TestProducts.MouseId, Quantity = 1 }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/orders", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
