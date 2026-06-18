using FluentAssertions;
using NRules;
using NRules.Fluent;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Infrastructure.Rules;
using OrderManagement.Infrastructure.Rules.Rules;

namespace OrderManagement.Tests.Domain;

public sealed class OrderTransitionRuleTests
{
    private readonly ISession _session;

    public OrderTransitionRuleTests()
    {
        var repository = new RuleRepository();
        repository.Load(x => x.From(typeof(PendingToConfirmedRule).Assembly));

        var factory = repository.Compile();
        _session = factory.CreateSession();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    public void Should_Allow_Valid_Transitions(OrderStatus currentStatus, OrderStatus targetStatus)
    {
        // Arrange
        var fact = new OrderTransitionFact
        {
            OrderId = Guid.NewGuid(),
            CurrentStatus = currentStatus,
            TargetStatus = targetStatus,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = UserRole.SellerAdmin,
            CustomerId = Guid.NewGuid()
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Confirmed)]
    public void Should_Reject_Invalid_Transitions(OrderStatus currentStatus, OrderStatus targetStatus)
    {
        // Arrange
        var fact = new OrderTransitionFact
        {
            OrderId = Guid.NewGuid(),
            CurrentStatus = currentStatus,
            TargetStatus = targetStatus,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = UserRole.SellerAdmin,
            CustomerId = Guid.NewGuid()
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(UserRole.Buyer)]
    [InlineData(UserRole.DevOps)]
    public void Should_Reject_Transitions_For_Unauthorized_Roles(UserRole unauthorizedRole)
    {
        // Arrange
        var fact = new OrderTransitionFact
        {
            OrderId = Guid.NewGuid(),
            CurrentStatus = OrderStatus.Pending,
            TargetStatus = OrderStatus.Confirmed,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = unauthorizedRole,
            CustomerId = Guid.NewGuid()
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(UserRole.SellerAdmin)]
    [InlineData(UserRole.SellerOperator)]
    [InlineData(UserRole.ApplicationAdmin)]
    public void Should_Allow_Transitions_For_Authorized_Roles(UserRole authorizedRole)
    {
        // Arrange
        var fact = new OrderTransitionFact
        {
            OrderId = Guid.NewGuid(),
            CurrentStatus = OrderStatus.Pending,
            TargetStatus = OrderStatus.Confirmed,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = authorizedRole,
            CustomerId = Guid.NewGuid()
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeTrue();
    }
}