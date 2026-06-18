using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Repositories;
using System.Data;
using System.Data.Common;

namespace OrderManagement.Tests.Infrastructure;

public sealed class OrderRepositoryTests
{
    private readonly Mock<IDbConnectionFactory> _connectionFactoryMock = new();
    private readonly Mock<IOrderRulesService> _orderRulesServiceMock = new();
    private readonly Mock<ILogger<OrderRepository>> _loggerMock = new();
    private readonly Mock<IActivityLogWriter> _activityLogWriterMock = new();
    private readonly OrderRepository _repository;

    public OrderRepositoryTests()
    {
        _repository = new OrderRepository(
            _connectionFactoryMock.Object,
            _orderRulesServiceMock.Object,
            _loggerMock.Object,
            _activityLogWriterMock.Object);
    }

    [Fact(Skip = "Requires real database - use integration tests instead")]
    public async Task CreateAsync_ShouldInsertOrderAndReturnResult()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var request = new CreateOrderPersistenceRequest
        {
            OrderId = orderId,
            CustomerId = customerId,
            CreatedBy = Guid.NewGuid(),
            ShippingAddress = "Test Address",
            Items = new[]
            {
                new CreateOrderPersistenceItem
                {
                    ProductId = productId,
                    Quantity = 2
                }
            },
            Now = now
        };

        var connectionMock = new Mock<DbConnection>();
        var transactionMock = new Mock<DbTransaction>();

        _connectionFactoryMock
            .Setup(x => x.CreateOpenConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionMock.Object);

        connectionMock
            .Setup(x => x.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactionMock.Object);

        // Act
        var result = await _repository.CreateAsync(request, CancellationToken.None);

        // Assert
        result.Id.Should().Be(orderId);
        result.OrderNumber.Should().StartWith("ORD-");
        result.TotalAmount.Should().BeGreaterThan(0);
        result.Status.Should().Be(OrderStatus.Pending.ToString());
        result.RowVersion.Should().BeGreaterThan(0);
    }

    [Fact(Skip = "Requires real database - use integration tests instead")]
    public async Task CancelAsync_ShouldUpdateOrderStatusAndReturnResult()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var rowVersion = 1L;
        var now = DateTimeOffset.UtcNow;

        var request = new CancelOrderPersistenceRequest
        {
            OrderId = orderId,
            ExpectedRowVersion = rowVersion,
            CancelledBy = Guid.NewGuid(),
            CancelledByRole = UserRole.Buyer,
            CancellationReason = OrderCancellationReason.CustomerRequested,
            RestoreStock = true,
            Reason = "Test cancellation",
            Now = now
        };

        var connectionMock = new Mock<DbConnection>();
        var transactionMock = new Mock<DbTransaction>();

        _connectionFactoryMock
            .Setup(x => x.CreateOpenConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionMock.Object);

        connectionMock
            .Setup(x => x.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactionMock.Object);

        // Act
        var result = await _repository.CancelAsync(request, CancellationToken.None);

        // Assert
        result.Id.Should().Be(orderId);
        result.OrderNumber.Should().StartWith("ORD-");
        result.PreviousStatus.Should().Be(OrderStatus.Pending.ToString());
        result.CurrentStatus.Should().Be(OrderStatus.Cancelled.ToString());
        result.CancellationReason.Should().Be(OrderCancellationReason.CustomerRequested.ToString());
        result.StockRestoreApplied.Should().BeTrue();
        result.RowVersion.Should().BeGreaterThan(rowVersion);
        result.StockRestored.Should().NotBeEmpty();
        result.PaymentRefundRequired.Should().BeFalse();
    }
}
