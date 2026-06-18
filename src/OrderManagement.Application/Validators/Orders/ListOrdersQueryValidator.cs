using FluentValidation;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Validators.Orders;

public sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQueryDto>
{
    public ListOrdersQueryValidator()
    {
        RuleFor(query => query.Status)
            .Must(BeValidOrderStatus)
            .WithMessage("Status is invalid.")
            .When(query => !string.IsNullOrWhiteSpace(query.Status));

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Page size must be between 1 and 100.");

        RuleFor(query => query)
            .Must(query =>
                query.FromDate is null ||
                query.ToDate is null ||
                query.FromDate <= query.ToDate)
            .WithMessage("From date must be less than or equal to to date.");
    }

    private static bool BeValidOrderStatus(string? status)
    {
        return Enum.TryParse<OrderStatus>(status, ignoreCase: true, out _);
    }
}