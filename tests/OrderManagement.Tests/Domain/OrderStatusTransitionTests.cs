using FluentAssertions;
using NRules;
using NRules.Fluent;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Infrastructure.Rules;
using OrderManagement.Infrastructure.Rules.Rules;

namespace OrderManagement.Tests.Domain;

public sealed class OrderStatusTransitionTests
{
    private readonly ISession _session;

    public OrderStatusTransitionTests()
    {
        var repository = new RuleRepository();
        repository.Load(x => x.From(typeof(PendingToConfirmedRule).Assembly));

        var factory = repository.Compile();
        _session = factory.CreateSession();
    }

    [Theory]
    [InlineData(UserRole.SellerAdmin)]
    [InlineData(UserRole.SellerOperator)]
    [InlineData(UserRole.ApplicationAdmin)]
    public void Should_Allow_Transition_When_Valid_Role(UserRole role)
    {
        // Arrange
        var fact = CreateTransitionFact(OrderStatus.Pending, OrderStatus.Confirmed, role);

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(UserRole.Buyer)]
    [InlineData(UserRole.DevOps)]
    public void Should_Reject_Transition_When_Invalid_Role(UserRole role)
    {
        // Arrange
        var fact = CreateTransitionFact(OrderStatus.Pending, OrderStatus.Confirmed, role);

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    public void Should_Allow_Valid_Lifecycle_Transitions(OrderStatus currentStatus, OrderStatus targetStatus)
    {
        // Arrange
        var fact = CreateTransitionFact(currentStatus, targetStatus, UserRole.SellerAdmin);

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)] // Skip Confirmed
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)] // Skip Confirmed and Shipped
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)] // Skip Shipped
    [InlineData(OrderStatus.Delivered, OrderStatus.Confirmed)] // Invalid backward transition
    public void Should_Reject_Invalid_Lifecycle_Transitions(OrderStatus currentStatus, OrderStatus targetStatus)
    {
        // Arrange
        var fact = CreateTransitionFact(currentStatus, targetStatus, UserRole.SellerAdmin);

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
    }

    private static OrderTransitionFact CreateTransitionFact(
        OrderStatus currentStatus,
        OrderStatus targetStatus,
        UserRole requestedByRole)
    {
        return new OrderTransitionFact
        {
            OrderId = Guid.NewGuid(),
            CurrentStatus = currentStatus,
            TargetStatus = targetStatus,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = requestedByRole,
            CustomerId = Guid.NewGuid()
        };
    }
}