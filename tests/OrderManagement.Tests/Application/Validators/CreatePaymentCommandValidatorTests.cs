using FluentAssertions;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.Validators.Payments;

namespace OrderManagement.Tests.Application.Validators;

public sealed class CreatePaymentCommandValidatorTests
{
    private readonly CreatePaymentCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldPass_WhenRequestIsValid()
    {
        var command = new CreatePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            Provider = "MockPayment",
            SimulateResult = "Success"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldFail_WhenOrderIdIsEmpty()
    {
        var command = new CreatePaymentCommand
        {
            OrderId = Guid.Empty,
            Provider = "MockPayment",
            SimulateResult = "Success"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreatePaymentCommand.OrderId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("Paid")]
    public void Validate_ShouldFail_WhenSimulateResultIsInvalid(string simulateResult)
    {
        var command = new CreatePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            Provider = "MockPayment",
            SimulateResult = simulateResult
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreatePaymentCommand.SimulateResult));
    }

    [Fact]
    public void Validate_ShouldFail_WhenProviderTooLong()
    {
        var command = new CreatePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            Provider = new string('A', 101),
            SimulateResult = "Success"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreatePaymentCommand.Provider));
    }
}