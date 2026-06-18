using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using OrderManagement.Api.Contracts.Payments;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace OrderManagement.IntegrationTests.Api;

public sealed class PaymentsApiTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public PaymentsApiTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListPayments_ForNonExistentOrder_ShouldReturnNotFound()
    {
        // Arrange
        await _factory.ResetDatabaseAsync();
        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "appadmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}/payments");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatePayment_ForNonExistentOrder_ShouldReturnNotFound()
    {
        // Arrange
        await _factory.ResetDatabaseAsync();
        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "buyer1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var request = new CreatePaymentRequest
        {
            Provider = "CreditCard",
            SimulateResult = "Success"
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/orders/{Guid.NewGuid()}/payments", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
