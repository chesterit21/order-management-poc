using FluentValidation;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Validators.Stores;

public sealed class CreateStoreOperatorCommandValidator : AbstractValidator<CreateStoreOperatorCommand>
{
    public CreateStoreOperatorCommandValidator()
    {
        RuleFor(command => command.StoreId)
            .NotEmpty()
            .WithMessage("Store id is required.");

        RuleFor(command => command.Username)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Username is required.")
            .MaximumLength(100)
            .WithMessage("Username cannot be longer than 100 characters.");

        RuleFor(command => command.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Display name is required.")
            .MaximumLength(150)
            .WithMessage("Display name cannot be longer than 150 characters.");

        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Password is required.")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters.")
            .MaximumLength(200)
            .WithMessage("Password cannot be longer than 200 characters.");
    }
}