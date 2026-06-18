using FluentValidation;
using OrderManagement.Application.DTOs.Dashboard;

namespace OrderManagement.Application.Validators.Dashboard;

public sealed class BackofficeDashboardSummaryQueryDtoValidator
    : AbstractValidator<BackofficeDashboardSummaryQueryDto>
{
    public BackofficeDashboardSummaryQueryDtoValidator()
    {
        RuleFor(query => query.StoreId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("StoreId must be a valid GUID.");

        RuleFor(query => query.LowStockThreshold)
            .InclusiveBetween(0, 100_000)
            .WithMessage("Low stock threshold must be between 0 and 100000.");
    }
}