using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class SetProductStatusCommandValidator : AbstractValidator<SetProductStatusCommand>
{
    public SetProductStatusCommandValidator()
    {
        RuleFor(command => command.ProductId)
            .NotEmpty()
            .WithMessage("Product id is required.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");
    }
}