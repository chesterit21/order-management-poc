using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using OrderManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace OrderManagement.IntegrationTests.Api;

public sealed class ProductsApiTests : IClassFixture<OrderManagementApiFactory>
{
    private readonly OrderManagementApiFactory _factory;

    public ProductsApiTests(OrderManagementApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProducts_ShouldReturnOk()
    {
        // Arrange
        await _factory.ResetDatabaseAsync();
        var client = _factory.Client;

        // Act
        var response = await client.GetAsync("/api/v1/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
    
    [Fact]
    public async Task GetProductById_WhenProductExists_ShouldReturnOk()
    {
        // Arrange
        await _factory.ResetDatabaseAsync();
        var client = _factory.Client;

        // Act
        var response = await client.GetAsync($"/api/v1/products/{TestProducts.MouseId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProductById_WhenProductDoesNotExist_ShouldReturnNotFound()
    {
        // Arrange
        await _factory.ResetDatabaseAsync();
        var client = _factory.Client;

        // Act
        var response = await client.GetAsync($"/api/v1/products/{System.Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
