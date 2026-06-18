using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(command => command.ProductId)
            .NotEmpty()
            .WithMessage("Product id is required.");

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

        RuleFor(command => command.Price)
            .GreaterThan(0)
            .WithMessage("Price must be greater than zero.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");
    }
}