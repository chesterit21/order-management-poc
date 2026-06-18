using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Services;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Tests.Application.Services;

public sealed class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<ICurrentUserContext> _currentUserContextMock = new();
    private readonly Mock<IRequestHashService> _requestHashServiceMock = new();
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock = new();
    private readonly Mock<IClock> _clockMock = new();
    private readonly Mock<IValidator<CreateOrderCommand>> _createValidatorMock = new();
    private readonly Mock<IValidator<ListOrdersQueryDto>> _listValidatorMock = new();
    private readonly Mock<IValidator<UpdateOrderStatusCommand>> _updateStatusValidatorMock = new();
    private readonly Mock<IValidator<CancelOrderCommand>> _cancelValidatorMock = new();
    private readonly Mock<IOrderCancellationPolicy> _cancellationPolicyMock = new();
    private readonly Mock<IActivityLogWriter> _activityLogWriterMock = new();
    private readonly ILogger<OrderService> _logger = NullLogger<OrderService>.Instance;

    private readonly Guid _currentUserId = Guid.NewGuid();
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _currentUserContextMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserContextMock.Setup(x => x.UserId).Returns(_currentUserId);
        _currentUserContextMock.Setup(x => x.Role).Returns(UserRole.Buyer);

        // Setup all validators to return valid by default
        _createValidatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _listValidatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<ListOrdersQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _updateStatusValidatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<UpdateOrderStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _cancelValidatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<CancelOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _service = new OrderService(
            _orderRepositoryMock.Object,
            _currentUserContextMock.Object,
            _requestHashServiceMock.Object,
            _idempotencyServiceMock.Object,
            _clockMock.Object,
            _createValidatorMock.Object,
            _listValidatorMock.Object,
            _updateStatusValidatorMock.Object,
            _cancelValidatorMock.Object,
            _cancellationPolicyMock.Object,
            _logger,
            _activityLogWriterMock.Object);
    }

    [Fact]
    public async Task CancelAsync_ShouldUseCancellationPolicy_AndDelegateToRepository()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var expectedRowVersion = 1L;
        var cancellationReason = "CustomerRequested";
        var freeTextReason = "Changed my mind";
        var currentRole = UserRole.Buyer;

        var cancellationDecision = new OrderCancellationDecision
        {
            CancellationReason = OrderCancellationReason.CustomerRequested,
            RestoreStock = true,
            ReasonText = "Cancellation reason: CustomerRequested. Stock restored. Note: Changed my mind"
        };

        _cancellationPolicyMock
            .Setup(x => x.Resolve(cancellationReason, freeTextReason, currentRole, true))
            .Returns(cancellationDecision);

        var repositoryResult = new CancelOrderResult
        {
            Id = orderId,
            OrderNumber = "ORD-20230101-000001",
            PreviousStatus = OrderStatus.Pending.ToString(),
            CurrentStatus = OrderStatus.Cancelled.ToString(),
            CancellationReason = OrderCancellationReason.CustomerRequested.ToString(),
            StockRestoreApplied = true,
            RowVersion = 2L,
            StockRestored = Array.Empty<StockRestoredResult>(),
            StockNotRestored = Array.Empty<StockNotRestoredResult>(),
            PaymentRefundRequired = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _orderRepositoryMock
            .Setup(x => x.CancelAsync(It.IsAny<CancelOrderPersistenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(repositoryResult);

        var command = new CancelOrderCommand
        {
            OrderId = orderId,
            ExpectedRowVersion = expectedRowVersion,
            CancellationReason = cancellationReason,
            Reason = freeTextReason
        };

        // Act
        var result = await _service.CancelAsync(command, CancellationToken.None);

        // Assert
        _cancellationPolicyMock.Verify(x => x.Resolve(cancellationReason, freeTextReason, currentRole, true), Times.Once);
        _orderRepositoryMock.Verify(x => x.CancelAsync(It.Is<CancelOrderPersistenceRequest>(r =>
            r.OrderId == orderId &&
            r.ExpectedRowVersion == expectedRowVersion &&
            r.CancellationReason == OrderCancellationReason.CustomerRequested &&
            r.RestoreStock == true &&
            r.Reason == cancellationDecision.ReasonText),
            It.IsAny<CancellationToken>()), Times.Once);

        result.Id.Should().Be(orderId);
        result.OrderNumber.Should().Be("ORD-20230101-000001");
        result.PreviousStatus.Should().Be(OrderStatus.Pending.ToString());
        result.CurrentStatus.Should().Be(OrderStatus.Cancelled.ToString());
        result.CancellationReason.Should().Be(OrderCancellationReason.CustomerRequested.ToString());
        result.StockRestoreApplied.Should().BeTrue();
        result.RowVersion.Should().Be(2L);
        result.StockRestored.Should().BeEmpty();
        result.StockNotRestored.Should().BeEmpty();
        result.PaymentRefundRequired.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_ShouldThrowForbiddenException_WhenCustomerUsesInvalidReason()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var expectedRowVersion = 1L;
        var cancellationReason = "StockUnavailable";
        var currentRole = UserRole.Buyer;

        _cancellationPolicyMock
            .Setup(x => x.Resolve(cancellationReason, null, currentRole, true))
            .Throws(new ForbiddenAppException("User cannot use this reason"));

        var command = new CancelOrderCommand
        {
            OrderId = orderId,
            ExpectedRowVersion = expectedRowVersion,
            CancellationReason = cancellationReason
        };

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenAppException>(() => _service.CancelAsync(command, CancellationToken.None));

        _cancellationPolicyMock.Verify(x => x.Resolve(cancellationReason, null, currentRole, true), Times.Once);
        _orderRepositoryMock.Verify(x => x.CancelAsync(It.IsAny<CancelOrderPersistenceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldValidateAndUpdateOrderStatusSuccessfully()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var rowVersion = 1L;
        var targetStatus = "Confirmed";
        var now = DateTimeOffset.UtcNow;

        _clockMock.Setup(x => x.UtcNow).Returns(now);
        _currentUserContextMock.Setup(x => x.Role).Returns(UserRole.ApplicationAdmin);

        var updateResult = new UpdateOrderStatusResult
        {
            Id = orderId,
            OrderNumber = "ORD-20230101-000001",
            PreviousStatus = OrderStatus.Pending.ToString(),
            CurrentStatus = OrderStatus.Confirmed.ToString(),
            RowVersion = 2L,
            UpdatedAt = now
        };

        _orderRepositoryMock
            .Setup(x => x.UpdateStatusAsync(It.IsAny<UpdateOrderStatusPersistenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateResult);

        var command = new UpdateOrderStatusCommand
        {
            OrderId = orderId,
            TargetStatus = targetStatus,
            ExpectedRowVersion = rowVersion,
            Reason = "Test status update",
            CurrentUserId = _currentUserId.ToString(),
            CurrentUserRole = UserRole.ApplicationAdmin.ToString()
        };

        // Act
        var result = await _service.UpdateStatusAsync(command, CancellationToken.None);

        // Assert
        _orderRepositoryMock.Verify(x => x.UpdateStatusAsync(It.Is<UpdateOrderStatusPersistenceRequest>(r =>
            r.OrderId == orderId &&
            r.TargetStatus == OrderStatus.Confirmed &&
            r.ExpectedRowVersion == rowVersion &&
            r.Reason == "Test status update" &&
            r.Now == now), It.IsAny<CancellationToken>()), Times.Once);

        result.Id.Should().Be(orderId);
        result.OrderNumber.Should().Be("ORD-20230101-000001");
        result.PreviousStatus.Should().Be(OrderStatus.Pending.ToString());
        result.CurrentStatus.Should().Be(OrderStatus.Confirmed.ToString());
        result.RowVersion.Should().Be(2L);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldThrowConflict_WhenRowVersionMismatch()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var rowVersion = 1L;
        var targetStatus = "Confirmed";

        _currentUserContextMock.Setup(x => x.Role).Returns(UserRole.ApplicationAdmin);

        _orderRepositoryMock
            .Setup(x => x.UpdateStatusAsync(It.IsAny<UpdateOrderStatusPersistenceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyAppException("Row version mismatch"));

        var command = new UpdateOrderStatusCommand
        {
            OrderId = orderId,
            TargetStatus = targetStatus,
            ExpectedRowVersion = rowVersion,
            Reason = "Test status update",
            CurrentUserId = _currentUserId.ToString(),
            CurrentUserRole = UserRole.ApplicationAdmin.ToString()
        };

        // Act & Assert
        await _service.Awaiting(x => x.UpdateStatusAsync(command, CancellationToken.None))
            .Should().ThrowAsync<ConcurrencyAppException>();

        _orderRepositoryMock.Verify(x => x.UpdateStatusAsync(It.IsAny<UpdateOrderStatusPersistenceRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
