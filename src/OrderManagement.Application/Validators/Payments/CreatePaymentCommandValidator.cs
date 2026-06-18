using FluentValidation;
using OrderManagement.Application.DTOs.Payments;

namespace OrderManagement.Application.Validators.Payments;

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.Provider)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Payment provider is required.")
            .MaximumLength(100)
            .WithMessage("Payment provider cannot be longer than 100 characters.");

        RuleFor(command => command.SimulateResult)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Simulate result is required.")
            .Must(BeValidSimulationResult)
            .WithMessage("Simulate result must be Success or Failed.");
    }

    private static bool BeValidSimulationResult(string value)
    {
        return Enum.TryParse<PaymentSimulationResult>(value, ignoreCase: true, out _);
    }
}