using FluentAssertions;
using OrderManagement.Application.Exceptions;
using OrderManagement.Application.Services;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Tests.Application.Services;

public sealed class OrderCancellationPolicyTests
{
    private readonly OrderCancellationPolicy _policy = new();

    [Fact]
    public void Resolve_ShouldDefaultCustomerToCustomerRequested_AndRestoreStock()
    {
        var result = _policy.Resolve(
            cancellationReason: null,
            freeTextReason: null,
            currentRole: UserRole.Buyer,
            isBuyerInitiated: true);

        result.CancellationReason.Should().Be(OrderCancellationReason.CustomerRequested);
        result.RestoreStock.Should().BeTrue();
        result.ReasonText.Should().Contain("Stock restored");
    }

    [Fact]
    public void Resolve_ShouldDefaultAdminToOperationalIssue_AndRestoreStock()
    {
        var result = _policy.Resolve(
            cancellationReason: null,
            freeTextReason: "Admin cancel",
            currentRole: UserRole.ApplicationAdmin,
            isBuyerInitiated: false);

        result.CancellationReason.Should().Be(OrderCancellationReason.OperationalIssue);
        result.RestoreStock.Should().BeTrue();
        result.ReasonText.Should().Contain("Admin cancel");
    }

    [Theory]
    [InlineData("StockUnavailable")]
    [InlineData("InventoryMismatch")]
    public void Resolve_ShouldNotRestoreStock_ForPhysicalStockProblem(string reason)
    {
        var result = _policy.Resolve(
            cancellationReason: reason,
            freeTextReason: "Warehouse confirmed no stock",
            currentRole: UserRole.ApplicationAdmin,
            isBuyerInitiated: false);

        result.RestoreStock.Should().BeFalse();
        result.ReasonText.Should().Contain("Stock was not restored");
    }

    [Fact]
    public void Resolve_ShouldRejectBuyerUsingStockUnavailableReason()
    {
        var act = () => _policy.Resolve(
            cancellationReason: "StockUnavailable",
            freeTextReason: null,
            currentRole: UserRole.Buyer,
            isBuyerInitiated: true);

        act.Should().Throw<ForbiddenAppException>();
    }

    [Fact]
    public void Resolve_ShouldRejectInvalidReason()
    {
        var act = () => _policy.Resolve(
            cancellationReason: "BadReason",
            freeTextReason: null,
            currentRole: UserRole.ApplicationAdmin,
            isBuyerInitiated: false);

        act.Should().Throw<BusinessRuleAppException>();
    }

    [Fact]
    public void Resolve_ShouldHandleEmptyStringReasonAsNull()
    {
        var result = _policy.Resolve(
            cancellationReason: "",
            freeTextReason: null,
            currentRole: UserRole.Buyer,
            isBuyerInitiated: true);

        result.CancellationReason.Should().Be(OrderCancellationReason.CustomerRequested);
        result.RestoreStock.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ShouldHandleWhitespaceOnlyReasonAsNull()
    {
        var result = _policy.Resolve(
            cancellationReason: "   ",
            freeTextReason: null,
            currentRole: UserRole.ApplicationAdmin,
            isBuyerInitiated: false);

        result.CancellationReason.Should().Be(OrderCancellationReason.OperationalIssue);
        result.RestoreStock.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ShouldHandleFreeTextReasonWithLeadingAndTrailingSpaces()
    {
        var result = _policy.Resolve(
            cancellationReason: "CustomerRequested",
            freeTextReason: "   Test note   ",
            currentRole: UserRole.Buyer,
            isBuyerInitiated: true);

        result.ReasonText.Should().Contain("Test note");
        result.ReasonText.Should().NotContain("   Test note   ");
    }
}