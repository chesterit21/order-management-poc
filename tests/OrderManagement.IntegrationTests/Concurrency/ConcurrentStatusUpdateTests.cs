using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class ConcurrentStatusUpdateTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public ConcurrentStatusUpdateTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TwoConcurrentStatusUpdates_ShouldNotResultInLostUpdate()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "appadmin");

        // Create an order first
        var orderId = await OrderApiHelper.CreateOrderAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1);

        // Get current row version
        var rowVersion = await DatabaseAssertHelper.GetOrderRowVersionAsync(
            _factory.ConnectionString,
            orderId);

        // Start two concurrent status updates
        var task1 = OrderApiHelper.UpdateStatusAsync(
            client,
            token,
            orderId,
            rowVersion,
            "Confirmed");

        var task2 = OrderApiHelper.UpdateStatusAsync(
            client,
            token,
            orderId,
            rowVersion,
            "Shipped");

        var responses = await Task.WhenAll(task1, task2);

        // One should succeed, one should fail with conflict
        responses.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        // Check final state
        var status = await DatabaseAssertHelper.GetOrderStatusAsync(_factory.ConnectionString, orderId);
        status.Should().BeOneOf("Confirmed", "Shipped");

        // Verify row version was incremented
        var finalRowVersion = await DatabaseAssertHelper.GetOrderRowVersionAsync(
            _factory.ConnectionString,
            orderId);
        finalRowVersion.Should().BeGreaterThan(rowVersion);
    }
}