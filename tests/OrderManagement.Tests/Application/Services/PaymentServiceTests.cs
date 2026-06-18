using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Payments;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Services;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Domain.Rules.Results;
using Xunit;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace OrderManagement.Tests.Application.Services;

public sealed class PaymentServiceTests
{
    private readonly Mock<IPaymentRepository> _paymentRepositoryMock = new();
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<ICurrentUserContext> _currentUserContextMock = new();
    private readonly Mock<IOrderRulesService> _orderRulesServiceMock = new();
    private readonly Mock<IClock> _clockMock = new();
    private readonly Mock<IValidator<CreatePaymentCommand>> _validatorMock = new();
    private readonly Mock<IActivityLogWriter> _activityLogWriterMock = new();

    private readonly PaymentService _service;
    private readonly Guid _currentUserId = Guid.NewGuid();

    public PaymentServiceTests()
    {
        _currentUserContextMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserContextMock.Setup(x => x.UserId).Returns(_currentUserId);
        _currentUserContextMock.Setup(x => x.Role).Returns(UserRole.Buyer);

        _validatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<CreatePaymentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _clockMock.Setup(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);

        _service = new PaymentService(
            _paymentRepositoryMock.Object,
            _orderRepositoryMock.Object,
            _currentUserContextMock.Object,
            _orderRulesServiceMock.Object,
            _clockMock.Object,
            _validatorMock.Object,
            NullLogger<PaymentService>.Instance,
            _activityLogWriterMock.Object);
    }

    private GetOrderResult CreateGetOrderResult(Guid orderId, Guid customerId, string status)
    {
        return new GetOrderResult
        {
            Id = orderId,
            OrderNumber = "ORD-001",
            CustomerId = customerId,
            CustomerName = "Test",
            Status = status,
            ShippingAddress = "Test",
            TotalAmount = 100,
            RowVersion = 1,
            Items = Array.Empty<OrderItemResult>(),
            StatusHistory = Array.Empty<OrderStatusHistoryResult>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            HasPaidPayment = false
        };
    }

    [Fact]
    public async Task CreateAsync_WhenValidationFails_ShouldThrowValidationAppException()
    {
        // Arrange
        var command = new CreatePaymentCommand { OrderId = Guid.NewGuid(), Provider = "Test", SimulateResult = "Success" };
        _validatorMock
            .Setup(x => x.ValidateAsync(command, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Provider", "Error") }));

        // Act
        var act = () => _service.CreateAsync(command);

        // Assert
        await act.Should().ThrowAsync<ValidationAppException>();
    }

    [Fact]
    public async Task CreateAsync_WhenOrderNotFound_ShouldThrowNotFoundAppException()
    {
        // Arrange
        var command = new CreatePaymentCommand { OrderId = Guid.NewGuid(), Provider = "Test", SimulateResult = "Success" };
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(command.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetOrderResult?)null);

        // Act
        var act = () => _service.CreateAsync(command);

        // Assert
        await act.Should().ThrowAsync<NotFoundAppException>();
    }

    [Fact]
    public async Task CreateAsync_WhenNotAllowedByRules_ShouldThrowBusinessRuleAppException()
    {
        // Arrange
        var command = new CreatePaymentCommand { OrderId = Guid.NewGuid(), Provider = "Test", SimulateResult = "Success" };
        var order = CreateGetOrderResult(command.OrderId, _currentUserId, "Cancelled");

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(command.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _orderRulesServiceMock
            .Setup(x => x.ValidatePayment(It.IsAny<PaymentFact>()))
            .Returns(RuleValidationResult.Rejected("ERR", "Not allowed"));

        // Act
        var act = () => _service.CreateAsync(command);

        // Assert
        await act.Should().ThrowAsync<BusinessRuleAppException>();
    }

    [Fact]
    public async Task CreateAsync_WhenValid_ShouldCreatePayment()
    {
        // Arrange
        var command = new CreatePaymentCommand { OrderId = Guid.NewGuid(), Provider = "Test", SimulateResult = "Success" };
        var order = CreateGetOrderResult(command.OrderId, _currentUserId, "Pending");

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(command.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _orderRulesServiceMock
            .Setup(x => x.ValidatePayment(It.IsAny<PaymentFact>()))
            .Returns(RuleValidationResult.Allowed());

        var expectedResult = new CreatePaymentResult 
        { 
            PaymentId = Guid.NewGuid(), 
            OrderId = command.OrderId, 
            Status = "Success",
            Provider = "Test",
            Amount = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            OrderStatus = "Pending"
        };
        
        _paymentRepositoryMock
            .Setup(x => x.CreateAsync(It.IsAny<CreatePaymentPersistenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.CreateAsync(command);

        // Assert
        result.Should().BeEquivalentTo(expectedResult);
        _paymentRepositoryMock.Verify(x => x.CreateAsync(It.IsAny<CreatePaymentPersistenceRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListByOrderIdAsync_WhenOrderNotFound_ShouldThrowNotFoundAppException()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetOrderResult?)null);

        // Act
        var act = () => _service.ListByOrderIdAsync(orderId);

        // Assert
        await act.Should().ThrowAsync<NotFoundAppException>();
    }

    [Fact]
    public async Task ListByOrderIdAsync_WhenBuyerIsNotOwner_ShouldThrowForbiddenAppException()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var order = CreateGetOrderResult(orderId, Guid.NewGuid(), "Pending"); // Different user
        
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        // Act
        var act = () => _service.ListByOrderIdAsync(orderId);

        // Assert
        await act.Should().ThrowAsync<ForbiddenAppException>();
    }

    [Fact]
    public async Task ListByOrderIdAsync_WhenValid_ShouldReturnPayments()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var order = CreateGetOrderResult(orderId, _currentUserId, "Pending");
        
        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var expectedResult = new PaymentListResult { OrderId = orderId, Payments = Array.Empty<PaymentResult>() };

        _paymentRepositoryMock
            .Setup(x => x.ListByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.ListByOrderIdAsync(orderId);

        // Assert
        result.Should().BeEquivalentTo(expectedResult);
    }
}
