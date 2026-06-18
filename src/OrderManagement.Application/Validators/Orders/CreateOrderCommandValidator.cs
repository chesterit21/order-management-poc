using FluentValidation;
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Application.Validators.Orders;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(command => command.IdempotencyKey)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Idempotency key is required.")
            .MaximumLength(200)
            .WithMessage("Idempotency key cannot be longer than 200 characters.");

        RuleFor(command => command.Endpoint)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Endpoint is required.")
            .MaximumLength(200)
            .WithMessage("Endpoint cannot be longer than 200 characters.");

        RuleFor(command => command.CustomerId)
            .NotEmpty()
            .WithMessage("Customer id is required.");

        RuleFor(command => command.ShippingAddress)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Shipping address is required.")
            .MaximumLength(1000)
            .WithMessage("Shipping address cannot be longer than 1000 characters.");

        RuleFor(command => command.Items)
            .NotEmpty()
            .WithMessage("Order must contain at least one item.");

        RuleFor(command => command.Items)
            .Must(items => items.Select(item => item.ProductId).Distinct().Count() == items.Count)
            .WithMessage("Duplicate product id is not allowed. Aggregate quantity per product before submitting.")
            .When(command => command.Items.Count > 0);

        RuleForEach(command => command.Items)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.ProductId)
                    .NotEmpty()
                    .WithMessage("Product id is required.");

                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0)
                    .WithMessage("Quantity must be greater than zero.")
                    .LessThanOrEqualTo(100_000)
                    .WithMessage("Quantity is too large.");
            });
    }
}