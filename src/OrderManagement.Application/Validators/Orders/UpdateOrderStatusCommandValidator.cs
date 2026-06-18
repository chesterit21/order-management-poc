using FluentValidation;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders;

public sealed class UpdateOrderStatusCommandValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty()
            .WithMessage("Order id is required.");

        RuleFor(command => command.TargetStatus)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Target status is required.")
            .Must(status => BeValidOrderStatus(status))
            .WithMessage("Target status is invalid.")
            .Must(status => NotCancelledStatus(status))
            .WithMessage("Use cancel endpoint to cancel an order.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidOrderStatus(string status)
    {
        return Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _);
    }

    private static bool NotCancelledStatus(string status)
    {
        return !Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) ||
               parsed != OrderStatus.Cancelled;
    }
}