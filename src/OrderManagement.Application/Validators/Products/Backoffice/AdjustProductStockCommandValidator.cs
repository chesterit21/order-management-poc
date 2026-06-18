using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class AdjustProductStockCommandValidator : AbstractValidator<AdjustProductStockCommand>
{
    public AdjustProductStockCommandValidator()
    {
        RuleFor(command => command.ProductId)
            .NotEmpty()
            .WithMessage("Product id is required.");

        RuleFor(command => command.AdjustmentType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Adjustment type is required.")
            .Must(BeValidAdjustmentType)
            .WithMessage("Adjustment type must be Increase, Decrease, or Set.");

        RuleFor(command => command.Quantity)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Quantity cannot be negative.");

        RuleFor(command => command)
            .Must(command =>
            {
                if (!Enum.TryParse<StockAdjustmentType>(
                        command.AdjustmentType,
                        ignoreCase: true,
                        out var adjustmentType))
                {
                    return true;
                }

                return adjustmentType == StockAdjustmentType.Set ||
                       command.Quantity > 0;
            })
            .WithMessage("Quantity must be greater than zero for Increase or Decrease adjustment.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");

        RuleFor(command => command.Reason)
            .MaximumLength(500)
            .WithMessage("Reason cannot be longer than 500 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Reason));
    }

    private static bool BeValidAdjustmentType(string value)
    {
        return Enum.TryParse<StockAdjustmentType>(value, ignoreCase: true, out _);
    }
}