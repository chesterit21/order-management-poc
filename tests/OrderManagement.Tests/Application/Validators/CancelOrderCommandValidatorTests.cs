using System;
using FluentValidation.TestHelper;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Validators.Orders;
using Xunit;

namespace OrderManagement.Tests.Application.Validators;

public sealed class CancelOrderCommandValidatorTests
{
    private readonly CancelOrderCommandValidator _validator = new();

    private CancelOrderCommand CreateValidCommand() => new()
    {
        OrderId = Guid.NewGuid(),
        ExpectedRowVersion = 1,
        CancellationReason = "CustomerRequested",
        Reason = "User changed mind"
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
    public void Validate_WhenExpectedRowVersionIsZero_ShouldHaveError()
    {
        var command = CreateValidCommand() with { ExpectedRowVersion = 0 };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ExpectedRowVersion).WithErrorMessage("Expected row version must be greater than zero.");
    }

    [Fact]
    public void Validate_WhenCancellationReasonIsInvalid_ShouldHaveError()
    {
        var command = CreateValidCommand() with { CancellationReason = "InvalidReason" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.CancellationReason).WithErrorMessage("Cancellation reason is invalid.");
    }
}
