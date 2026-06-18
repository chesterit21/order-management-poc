using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class ConcurrentStockDeductionTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public ConcurrentStockDeductionTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TwoConcurrentOrders_ShouldNotDeductStockMoreThanAvailable()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "buyer1");

        var task1 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            10,
            Guid.NewGuid().ToString("N"));

        var task2 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            10,
            Guid.NewGuid().ToString("N"));

        var responses = await Task.WhenAll(task1, task2);

        responses.Count(x => x.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var finalStock = await DatabaseAssertHelper.GetProductStockAsync(
            _factory.ConnectionString,
            TestProducts.MouseId);

        finalStock.Should().Be(5);

        var orderCount = await DatabaseAssertHelper.CountOrdersAsync(_factory.ConnectionString);
        orderCount.Should().Be(1);
    }
}