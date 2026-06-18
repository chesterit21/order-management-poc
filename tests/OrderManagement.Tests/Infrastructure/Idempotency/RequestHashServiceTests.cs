using FluentAssertions;
using OrderManagement.Infrastructure.Idempotency;

namespace OrderManagement.Tests.Infrastructure.Idempotency;

public sealed class RequestHashServiceTests
{
    private readonly RequestHashService _service = new();

    [Fact]
    public void ComputeHashFromJson_ShouldReturnSameHash_WhenObjectPropertyOrderDiffers()
    {
        const string json1 = """
                             {
                               "customerId": "customer-1",
                               "shippingAddress": "Address",
                               "items": [
                                 {
                                   "productId": "product-1",
                                   "quantity": 10
                                 }
                               ]
                             }
                             """;

        const string json2 = """
                             {
                               "items": [
                                 {
                                   "quantity": 10,
                                   "productId": "product-1"
                                 }
                               ],
                               "shippingAddress": "Address",
                               "customerId": "customer-1"
                             }
                             """;

        var hash1 = _service.ComputeHashFromJson(json1);
        var hash2 = _service.ComputeHashFromJson(json2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHashFromJson_ShouldReturnDifferentHash_WhenArrayOrderDiffers()
    {
        const string json1 = """
                             {
                               "items": [
                                 { "productId": "product-1", "quantity": 10 },
                                 { "productId": "product-2", "quantity": 5 }
                               ]
                             }
                             """;

        const string json2 = """
                             {
                               "items": [
                                 { "productId": "product-2", "quantity": 5 },
                                 { "productId": "product-1", "quantity": 10 }
                               ]
                             }
                             """;

        var hash1 = _service.ComputeHashFromJson(json1);
        var hash2 = _service.ComputeHashFromJson(json2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHashFromJson_ShouldReturnDifferentHash_WhenQuantityDiffers()
    {
        const string json1 = """
                             {
                               "items": [
                                 { "productId": "product-1", "quantity": 10 }
                               ]
                             }
                             """;

        const string json2 = """
                             {
                               "items": [
                                 { "productId": "product-1", "quantity": 11 }
                               ]
                             }
                             """;

        var hash1 = _service.ComputeHashFromJson(json1);
        var hash2 = _service.ComputeHashFromJson(json2);

        hash1.Should().NotBe(hash2);
    }
}