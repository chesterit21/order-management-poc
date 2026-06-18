using FluentValidation;
using OrderManagement.Application.DTOs.Products;

namespace OrderManagement.Application.Validators.Products;

public sealed class ProductListQueryDtoValidator : AbstractValidator<ProductListQueryDto>
{
    public ProductListQueryDtoValidator()
    {
        RuleFor(query => query.Search)
            .MaximumLength(100)
            .WithMessage("Search cannot be longer than 100 characters.")
            .When(query => !string.IsNullOrWhiteSpace(query.Search));

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Page size must be between 1 and 100.");
    }
}