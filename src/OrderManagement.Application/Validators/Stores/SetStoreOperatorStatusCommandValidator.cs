using FluentValidation;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Validators.Stores;

public sealed class SetStoreOperatorStatusCommandValidator : AbstractValidator<SetStoreOperatorStatusCommand>
{
    public SetStoreOperatorStatusCommandValidator()
    {
        RuleFor(command => command.StoreId)
            .NotEmpty()
            .WithMessage("Store id is required.");

        RuleFor(command => command.OperatorUserId)
            .NotEmpty()
            .WithMessage("Operator user id is required.");
    }
}