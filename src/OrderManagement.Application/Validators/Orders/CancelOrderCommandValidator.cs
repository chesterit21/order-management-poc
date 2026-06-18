using FluentValidation;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.CancellationReason)
            .Must(BeValidCancellationReason)
            .WithMessage("Cancellation reason is invalid.")
            .When(command => !string.IsNullOrWhiteSpace(command.CancellationReason));

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidCancellationReason(string? reason)
    {
        return Enum.TryParse<OrderCancellationReason>(reason, ignoreCase: true, out _);
    }
}