using FluentValidation;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Validators.Stores;

public sealed class OpenStoreCommandValidator : AbstractValidator<OpenStoreCommand>
{
    public OpenStoreCommandValidator()
    {
        RuleFor(command => command.StoreName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Store name is required.")
            .MaximumLength(150)
            .WithMessage("Store name cannot be longer than 150 characters.");

        RuleFor(command => command.Description)
            .MaximumLength(1000)
            .WithMessage("Store description cannot be longer than 1000 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Description));
    }
}