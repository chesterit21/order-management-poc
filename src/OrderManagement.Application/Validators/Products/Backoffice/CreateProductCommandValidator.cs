using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(command => command.StoreId)
            .NotEmpty()
            .WithMessage("Store id is required.");

        RuleFor(command => command.Sku)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("SKU is required.")
            .MaximumLength(100)
            .WithMessage("SKU cannot be longer than 100 characters.");

        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Product name is required.")
            .MaximumLength(200)
            .WithMessage("Product name cannot be longer than 200 characters.");

        RuleFor(command => command.Description)
            .MaximumLength(2000)
            .WithMessage("Description cannot be longer than 2000 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Description));

        RuleFor(command => command.StockQuantity)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Stock quantity cannot be negative.");

        RuleFor(command => command.Price)
            .GreaterThan(0)
            .WithMessage("Price must be greater than zero.");
    }
}