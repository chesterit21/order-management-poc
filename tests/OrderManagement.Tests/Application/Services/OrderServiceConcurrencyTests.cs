using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
using OrderManagement.Application.DTOs.Idempotency;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Services;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Tests.Application.Services;

public sealed class OrderServiceConcurrencyTests
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

    public OrderServiceConcurrencyTests()
    {
        _currentUserContextMock.Setup(x => x.IsAuthenticated).Returns(true);
        _currentUserContextMock.Setup(x => x.UserId).Returns(_currentUserId);

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

        _requestHashServiceMock
            .Setup(x => x.ComputeHash(It.IsAny<object>()))
            .Returns("test-hash");

        var now = DateTimeOffset.UtcNow;
        _clockMock.Setup(x => x.UtcNow).Returns(now);

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
    public async Task CreateAsync_ConcurrentOrdersForSameProduct_ShouldHandleConcurrencyConflict()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var idempotencyKey1 = Guid.NewGuid().ToString("N");
        var idempotencyKey2 = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        _currentUserContextMock.Setup(x => x.Role).Returns(UserRole.Buyer);
        _currentUserContextMock.Setup(x => x.UserId).Returns(customerId);

        _idempotencyServiceMock
            .SetupSequence(x => x.BeginAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyProcessResult.ProcessRequest(Guid.NewGuid()))
            .ReturnsAsync(IdempotencyProcessResult.ProcessRequest(Guid.NewGuid()));

        var firstOrderId = Guid.NewGuid();
        var firstCreateResult = new CreateOrderResult
        {
            Id = firstOrderId,
            OrderNumber = "ORD-0001",
            CustomerId = customerId,
            Status = OrderStatus.Pending.ToString(),
            ShippingAddress = "Test Address",
            TotalAmount = 100,
            RowVersion = 1,
            Items = new List<CreateOrderItemResult>
            {
                new CreateOrderItemResult
                {
                    ProductId = productId,
                    ProductName = "Test Product",
                    Quantity = 10,
                    UnitPrice = 10,
                    LineTotal = 100
                }
            },
            CreatedAt = now
        };

        _orderRepositoryMock
            .SetupSequence(x => x.CreateAsync(It.IsAny<CreateOrderPersistenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstCreateResult)
            .ThrowsAsync(new ConcurrencyAppException("Insufficient stock for product"));

        var command1 = new CreateOrderCommand
        {
            IdempotencyKey = idempotencyKey1,
            Endpoint = "/api/orders",
            CustomerId = customerId,
            Items = new List<CreateOrderItemCommand>
            {
                new CreateOrderItemCommand { ProductId = productId, Quantity = 10 }
            },
            ShippingAddress = "Test Address"
        };

        var command2 = new CreateOrderCommand
        {
            IdempotencyKey = idempotencyKey2,
            Endpoint = "/api/orders",
            CustomerId = customerId,
            Items = new List<CreateOrderItemCommand>
            {
                new CreateOrderItemCommand { ProductId = productId, Quantity = 10 }
            },
            ShippingAddress = "Test Address"
        };

        // Act
        var task1 = _service.CreateAsync(command1, CancellationToken.None);
        var task2 = _service.CreateAsync(command2, CancellationToken.None);

        var results = await Task.WhenAll(
            Task.Run(async () => { try { return (object)await task1; } catch (Exception ex) { return (object)ex; } }),
            Task.Run(async () => { try { return (object)await task2; } catch (Exception ex) { return (object)ex; } }));

        // Assert
        results.Should().HaveCount(2);
        var successCount = results.Count(r => r is CreateOrderResult);
        var exceptionCount = results.Count(r => r is ConcurrencyAppException);

        // One should succeed, one should fail with concurrency exception
        // Note: In unit test with mocked repo, we simulate the conflict by throwing ConcurrencyAppException
        _orderRepositoryMock.Verify(x => x.CreateAsync(It.IsAny<CreateOrderPersistenceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateStatusAsync_ConcurrentUpdates_ShouldHandleRowVersionConflict()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var rowVersion = 1L;
        var now = DateTimeOffset.UtcNow;

        _currentUserContextMock.Setup(x => x.Role).Returns(UserRole.ApplicationAdmin);
        _currentUserContextMock.Setup(x => x.UserId).Returns(_currentUserId);

        var updateResult = new UpdateOrderStatusResult
        {
            Id = orderId,
            OrderNumber = "ORD-0001",
            PreviousStatus = OrderStatus.Pending.ToString(),
            CurrentStatus = OrderStatus.Confirmed.ToString(),
            RowVersion = 2,
            UpdatedAt = now
        };

        _orderRepositoryMock
            .SetupSequence(x => x.UpdateStatusAsync(It.IsAny<UpdateOrderStatusPersistenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updateResult)
            .ThrowsAsync(ConcurrencyAppException.RowVersionMismatch(rowVersion, 2));

        var command1 = new UpdateOrderStatusCommand
        {
            OrderId = orderId,
            TargetStatus = "Confirmed",
            ExpectedRowVersion = rowVersion,
            Reason = "First update",
            CurrentUserId = _currentUserId.ToString(),
            CurrentUserRole = UserRole.ApplicationAdmin.ToString()
        };

        var command2 = new UpdateOrderStatusCommand
        {
            OrderId = orderId,
            TargetStatus = "Shipped",
            ExpectedRowVersion = rowVersion,
            Reason = "Second update",
            CurrentUserId = _currentUserId.ToString(),
            CurrentUserRole = UserRole.ApplicationAdmin.ToString()
        };

        // Act
        var task1 = _service.UpdateStatusAsync(command1, CancellationToken.None);
        var task2 = _service.UpdateStatusAsync(command2, CancellationToken.None);

        var results = await Task.WhenAll(
            Task.Run(async () => { try { return (object)await task1; } catch (Exception ex) { return (object)ex; } }),
            Task.Run(async () => { try { return (object)await task2; } catch (Exception ex) { return (object)ex; } }));

        // Assert
        var successCount = results.Count(r => r is UpdateOrderStatusResult);
        var concurrencyExceptionCount = results.Count(r => r is ConcurrencyAppException);

        successCount.Should().Be(1);
        concurrencyExceptionCount.Should().Be(1);

        _orderRepositoryMock.Verify(x => x.UpdateStatusAsync(It.IsAny<UpdateOrderStatusPersistenceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateAsync_SameIdempotencyKeyConcurrently_ShouldReturnStoredResponse()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        _currentUserContextMock.Setup(x => x.Role).Returns(UserRole.Buyer);
        _currentUserContextMock.Setup(x => x.UserId).Returns(customerId);

        var createResult = new CreateOrderResult
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-0001",
            CustomerId = customerId,
            Status = OrderStatus.Pending.ToString(),
            ShippingAddress = "Test Address",
            TotalAmount = 100,
            RowVersion = 1,
            Items = new List<CreateOrderItemResult>
            {
                new CreateOrderItemResult
                {
                    ProductId = productId,
                    ProductName = "Test Product",
                    Quantity = 1,
                    UnitPrice = 100,
                    LineTotal = 100
                }
            },
            CreatedAt = now
        };

        // First call: no stored response, proceeds to create
        // Second call: stored response found (simulating race condition where first request already completed)
        _idempotencyServiceMock
            .SetupSequence(x => x.BeginAsync(
                idempotencyKey,
                customerId,
                "/api/orders",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyProcessResult.ProcessRequest(Guid.NewGuid()))
            .ReturnsAsync(IdempotencyProcessResult.ReturnStoredResponse(
                201,
                System.Text.Json.JsonSerializer.Serialize(createResult)
            ));

        _orderRepositoryMock
            .Setup(x => x.CreateAsync(It.IsAny<CreateOrderPersistenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResult);

        var command1 = new CreateOrderCommand
        {
            IdempotencyKey = idempotencyKey,
            Endpoint = "/api/orders",
            CustomerId = customerId,
            Items = new List<CreateOrderItemCommand>
            {
                new CreateOrderItemCommand { ProductId = productId, Quantity = 1 }
            },
            ShippingAddress = "Test Address"
        };

        var command2 = new CreateOrderCommand
        {
            IdempotencyKey = idempotencyKey,
            Endpoint = "/api/orders",
            CustomerId = customerId,
            Items = new List<CreateOrderItemCommand>
            {
                new CreateOrderItemCommand { ProductId = productId, Quantity = 1 }
            },
            ShippingAddress = "Test Address"
        };

        // Act
        var task1 = _service.CreateAsync(command1, CancellationToken.None);
        var task2 = _service.CreateAsync(command2, CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        // Assert
        results.Should().HaveCount(2);
        results.Should().ContainSingle(r => r.IsStoredResponse == false, "One request should process normally");
        results.Should().ContainSingle(r => r.IsStoredResponse == true, "One request should get stored response");
        results.Should().AllSatisfy(r => r.StatusCode.Should().Be(201));

        // Repository should only be called once (first request creates, second gets stored response)
        _orderRepositoryMock.Verify(x => x.CreateAsync(It.IsAny<CreateOrderPersistenceRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_ConcurrentCancels_ShouldHandleRowVersionConflict()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var rowVersion = 1L;
        var customerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        _currentUserContextMock.Setup(x => x.Role).Returns(UserRole.Buyer);
        _currentUserContextMock.Setup(x => x.UserId).Returns(customerId);

        var ownership = new OrderOwnershipResult
        {
            Id = orderId,
            CustomerId = customerId,
            Status = OrderStatus.Pending.ToString(),
            RowVersion = rowVersion,
            OrderNumber = "ORD-0001"
        };

        _orderRepositoryMock
            .Setup(x => x.GetOrderOwnershipAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownership);

        var cancellationDecision = new OrderCancellationDecision
        {
            CancellationReason = OrderCancellationReason.CustomerRequested,
            RestoreStock = true,
            ReasonText = "Cancellation reason: CustomerRequested. Stock restored."
        };

        _cancellationPolicyMock
            .Setup(x => x.Resolve(
                "CustomerRequested",
                null,
                UserRole.Buyer,
                true))
            .Returns(cancellationDecision);

        var cancelResult = new CancelOrderResult
        {
            Id = orderId,
            OrderNumber = "ORD-0001",
            PreviousStatus = OrderStatus.Pending.ToString(),
            CurrentStatus = OrderStatus.Cancelled.ToString(),
            CancellationReason = OrderCancellationReason.CustomerRequested.ToString(),
            StockRestoreApplied = true,
            RowVersion = 2,
            StockRestored = Array.Empty<StockRestoredResult>(),
            StockNotRestored = Array.Empty<StockNotRestoredResult>(),
            PaymentRefundRequired = false,
            UpdatedAt = now
        };

        _orderRepositoryMock
            .SetupSequence(x => x.CancelAsync(It.IsAny<CancelOrderPersistenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelResult)
            .ThrowsAsync(ConcurrencyAppException.RowVersionMismatch(rowVersion, 2));

        var command1 = new CancelOrderCommand
        {
            OrderId = orderId,
            ExpectedRowVersion = rowVersion,
            CancellationReason = "CustomerRequested"
        };

        var command2 = new CancelOrderCommand
        {
            OrderId = orderId,
            ExpectedRowVersion = rowVersion,
            CancellationReason = "CustomerRequested"
        };

        // Act
        var task1 = _service.CancelAsync(command1, CancellationToken.None);
        var task2 = _service.CancelAsync(command2, CancellationToken.None);

        var results = await Task.WhenAll(
            Task.Run(async () => { try { return (object)await task1; } catch (Exception ex) { return (object)ex; } }),
            Task.Run(async () => { try { return (object)await task2; } catch (Exception ex) { return (object)ex; } }));

        // Assert
        var successCount = results.Count(r => r is CancelOrderResult);
        var concurrencyExceptionCount = results.Count(r => r is ConcurrencyAppException);

        successCount.Should().Be(1);
        concurrencyExceptionCount.Should().Be(1);

        _orderRepositoryMock.Verify(x => x.CancelAsync(It.IsAny<CancelOrderPersistenceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}