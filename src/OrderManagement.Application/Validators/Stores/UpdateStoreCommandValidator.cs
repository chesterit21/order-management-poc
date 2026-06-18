using FluentValidation;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Validators.Stores;

public sealed class UpdateStoreCommandValidator : AbstractValidator<UpdateStoreCommand>
{
    public UpdateStoreCommandValidator()
    {
        RuleFor(command => command.StoreId)
            .NotEmpty()
            .WithMessage("Store id is required.");

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