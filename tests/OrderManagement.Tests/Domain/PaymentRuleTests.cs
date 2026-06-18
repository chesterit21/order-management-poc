using FluentAssertions;
using NRules;
using NRules.Fluent;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Infrastructure.Rules;
using OrderManagement.Infrastructure.Rules.Rules;

namespace OrderManagement.Tests.Domain;

public sealed class PaymentRuleTests
{
    private readonly ISession _session;

    public PaymentRuleTests()
    {
        var repository = new RuleRepository();
        repository.Load(x => x.From(typeof(PaymentAllowedRule).Assembly));

        var factory = repository.Compile();
        _session = factory.CreateSession();
    }

    [Fact]
    public void Should_Allow_Payment_When_Pending_Status_And_Authorized_Role()
    {
        // Arrange
        var fact = new PaymentFact
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CurrentOrderStatus = OrderStatus.Pending,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = UserRole.Buyer,
            HasExistingPaidPayment = false
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Should_Reject_Payment_When_Has_Existing_Paid_Payment()
    {
        // Arrange
        var fact = new PaymentFact
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CurrentOrderStatus = OrderStatus.Pending,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = UserRole.Buyer,
            HasExistingPaidPayment = true
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
        fact.ErrorCode.Should().NotBeNull();
    }

    [Fact]
    public void Should_Reject_Payment_When_Non_Pending_Status()
    {
        // Arrange
        var fact = new PaymentFact
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CurrentOrderStatus = OrderStatus.Confirmed,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = UserRole.Buyer,
            HasExistingPaidPayment = false
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
        fact.ErrorCode.Should().NotBeNull();
    }

    [Theory]
    [InlineData(UserRole.SellerAdmin)]
    [InlineData(UserRole.ApplicationAdmin)]
    public void Should_Allow_Payment_For_Authorized_Roles(UserRole authorizedRole)
    {
        // Arrange
        var fact = new PaymentFact
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CurrentOrderStatus = OrderStatus.Pending,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = authorizedRole,
            HasExistingPaidPayment = false
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(UserRole.SellerOperator)]
    [InlineData(UserRole.DevOps)]
    public void Should_Reject_Payment_For_Unauthorized_Roles(UserRole unauthorizedRole)
    {
        // Arrange
        var fact = new PaymentFact
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CurrentOrderStatus = OrderStatus.Pending,
            RequestedByUserId = Guid.NewGuid(),
            RequestedByRole = unauthorizedRole,
            HasExistingPaidPayment = false
        };

        _session.Insert(fact);

        // Act
        _session.Fire();

        // Assert
        fact.IsAllowed.Should().BeFalse();
    }
}