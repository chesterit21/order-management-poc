using FluentValidation;
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders.Backoffice;

public sealed class BackofficeCancelOrderCommandValidator
    : AbstractValidator<BackofficeCancelOrderCommand>
{
    public BackofficeCancelOrderCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.CancellationReason)
            .Must(BeValidReason)
            .WithMessage("Cancellation reason is invalid.")
            .When(command => !string.IsNullOrWhiteSpace(command.CancellationReason));

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidReason(string? reason)
    {
        return Enum.TryParse<OrderCancellationReason>(reason, ignoreCase: true, out _);
    }
}