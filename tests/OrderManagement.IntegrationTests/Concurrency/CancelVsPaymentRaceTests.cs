using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class CancelVsPaymentRaceTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public CancelVsPaymentRaceTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CancelAndPaySameOrder_Concurrently_ShouldResultInConsistentState()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "buyer1");

        // First create an order
        var orderId = await OrderApiHelper.CreateOrderAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1);

        // Get current row version for cancellation
        var rowVersion = await DatabaseAssertHelper.GetOrderRowVersionAsync(
            _factory.ConnectionString,
            orderId);

        // Start concurrent cancellation and payment
        var cancelTask = OrderApiHelper.CancelAsync(
            client,
            token,
            orderId,
            rowVersion,
            "CustomerRequested");

        var payTask = OrderApiHelper.PayAsync(
            client,
            token,
            orderId,
            "Success");

        var results = await Task.WhenAll(cancelTask, payTask);

        // One should succeed, one should fail with conflict
        var successCount = results.Count(x => x.StatusCode == HttpStatusCode.OK || x.StatusCode == HttpStatusCode.Created);
        var conflictCount = results.Count(x => x.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1);
        conflictCount.Should().Be(1);

        // Check final state
        var status = await DatabaseAssertHelper.GetOrderStatusAsync(_factory.ConnectionString, orderId);
        status.Should().BeOneOf("Cancelled", "Paid");

        // Check stock restoration - only if cancelled
        var stock = await DatabaseAssertHelper.GetProductStockAsync(
            _factory.ConnectionString,
            TestProducts.MouseId);

        // If cancelled, stock should be restored to 15
        // If paid, stock should remain at 14
        if (status == "Cancelled")
        {
            stock.Should().Be(15);
        }
        else
        {
            stock.Should().Be(14);
        }
    }
}