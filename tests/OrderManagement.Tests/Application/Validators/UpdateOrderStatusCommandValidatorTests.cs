using System;
using FluentValidation.TestHelper;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Validators.Orders;
using Xunit;

namespace OrderManagement.Tests.Application.Validators;

public sealed class UpdateOrderStatusCommandValidatorTests
{
    private readonly UpdateOrderStatusCommandValidator _validator = new();

    private UpdateOrderStatusCommand CreateValidCommand() => new()
    {
        OrderId = Guid.NewGuid(),
        TargetStatus = "Shipped",
        ExpectedRowVersion = 1,
        Reason = "Order dispatched"
    };

    [Fact]
    public void Validate_WhenValid_ShouldNotHaveAnyErrors()
    {
        var command = CreateValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenOrderIdIsEmpty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { OrderId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.OrderId).WithErrorMessage("Order id is required.");
    }

    [Fact]
    public void Validate_WhenTargetStatusIsEmpty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { TargetStatus = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.TargetStatus).WithErrorMessage("Target status is required.");
    }

    [Fact]
    public void Validate_WhenTargetStatusIsInvalid_ShouldHaveError()
    {
        var command = CreateValidCommand() with { TargetStatus = "InvalidStatus" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.TargetStatus).WithErrorMessage("Target status is invalid.");
    }

    [Fact]
    public void Validate_WhenTargetStatusIsCancelled_ShouldHaveError()
    {
        var command = CreateValidCommand() with { TargetStatus = "Cancelled" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.TargetStatus).WithErrorMessage("Use cancel endpoint to cancel an order.");
    }

    [Fact]
    public void Validate_WhenExpectedRowVersionIsZero_ShouldHaveError()
    {
        var command = CreateValidCommand() with { ExpectedRowVersion = 0 };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ExpectedRowVersion).WithErrorMessage("Expected row version must be greater than zero.");
    }
}
