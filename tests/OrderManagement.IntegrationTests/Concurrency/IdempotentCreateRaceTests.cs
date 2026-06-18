using System.Net;
using FluentAssertions;
using OrderManagement.IntegrationTests.Helpers;
using OrderManagement.IntegrationTests.Infrastructure;

namespace OrderManagement.IntegrationTests.Concurrency;

public sealed class IdempotentCreateRaceTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public IdempotentCreateRaceTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SameIdempotencyKey_UsedMultipleTimes_ShouldCreateOnlyOneOrder()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "buyer1");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var task1 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1,
            idempotencyKey);

        var task2 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1,
            idempotencyKey);

        var responses = await Task.WhenAll(task1, task2);

        responses.Count(x => x.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var orderCount = await DatabaseAssertHelper.CountOrdersAsync(_factory.ConnectionString);
        orderCount.Should().Be(1);

        var stock = await DatabaseAssertHelper.GetProductStockAsync(
            _factory.ConnectionString,
            TestProducts.MouseId);
        stock.Should().Be(14);
    }

    [Fact]
    public async Task DifferentIdempotencyKeys_ShouldCreateMultipleOrders()
    {
        await _factory.ResetDatabaseAsync();

        var client = _factory.Client;
        var token = await AuthHelper.LoginAsync(client, "buyer1");
        var key1 = Guid.NewGuid().ToString("N");
        var key2 = Guid.NewGuid().ToString("N");

        var task1 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1,
            key1);

        var task2 = OrderApiHelper.CreateOrderRawAsync(
            client,
            token,
            TestUsers.Customer1Id,
            TestProducts.MouseId,
            1,
            key2);

        var responses = await Task.WhenAll(task1, task2);

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Created));

        var orderCount = await DatabaseAssertHelper.CountOrdersAsync(_factory.ConnectionString);
        orderCount.Should().Be(2);

        var stock = await DatabaseAssertHelper.GetProductStockAsync(
            _factory.ConnectionString,
            TestProducts.MouseId);
        stock.Should().Be(13);
    }
}