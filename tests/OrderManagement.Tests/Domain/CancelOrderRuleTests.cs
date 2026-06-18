using FluentAssertions;
using NRules;
using NRules.Fluent;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Infrastructure.Rules;
using OrderManagement.Infrastructure.Rules.Rules;

namespace OrderManagement.Tests.Domain;

public sealed class CancelOrderRuleTests
{
    private readonly ISession _session;

    public CancelOrderRuleTests()
    {
        var repository = new RuleRepository();
        repository.Load(x => x.From(typeof(CancelAllowedRule).Assembly));

        var factory = repository.Compile();
        _session = factory.CreateSession();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    public void Should_Allow_Cancel_When_Valid_Status_And_Permission(OrderStatus status)
    {
        // Arrange
        var fact = CreateCancelFact(status, OrderCancellationReason.CustomerRequested, UserRole.Buyer);

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void Should_Reject_Cancel_When_Invalid_Status(OrderStatus status)
    {
        // Arrange
        var fact = CreateCancelFact(status, OrderCancellationReason.CustomerRequested, UserRole.Buyer);

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Should_Reject_Cancel_When_DevOps_Role()
    {
        // Arrange
        var fact = CreateCancelFact(OrderStatus.Pending, OrderCancellationReason.CustomerRequested, UserRole.DevOps);

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Should_Allow_Cancel_When_ApplicationAdmin_Role()
    {
        // Arrange
        var fact = CreateCancelFact(OrderStatus.Pending, OrderCancellationReason.OperationalIssue, UserRole.ApplicationAdmin);

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeTrue();
    }

    private static CancelOrderFact CreateCancelFact(
        OrderStatus currentStatus,
        OrderCancellationReason cancellationReason,
        UserRole requestedByRole)
    {
        return new CancelOrderFact
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CurrentStatus = currentStatus,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = requestedByRole
        };
    }
}