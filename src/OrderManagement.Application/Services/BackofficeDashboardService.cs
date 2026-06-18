using FluentValidation;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Dashboard;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Dashboard;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class BackofficeDashboardService(
    IBackofficeDashboardRepository repository,
    IStoreRepository storeRepository,
    IStoreAuthorizationService storeAuthorizationService,
    ICurrentUserContext currentUserContext,
    IClock clock,
    IValidator<BackofficeDashboardSummaryQueryDto> validator) : IBackofficeDashboardService
{
    private readonly IBackofficeDashboardRepository _repository = repository;
    private readonly IStoreRepository _storeRepository = storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService = storeAuthorizationService;
    private readonly ICurrentUserContext _currentUserContext = currentUserContext;
    private readonly IClock _clock = clock;
    private readonly IValidator<BackofficeDashboardSummaryQueryDto> _validator = validator;

    public async Task<BackofficeDashboardSummaryDto> GetSummaryAsync(
        BackofficeDashboardSummaryQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(query, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice dashboard query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        if (query.StoreId is not null)
        {
            await _storeAuthorizationService.EnsureCanOperateStoreAsync(
                query.StoreId.Value,
                cancellationToken);
        }

        var allowedStoreIds = await ResolveAllowedStoreIdsAsync(cancellationToken);

        return await _repository.GetSummaryAsync(
            query,
            allowedStoreIds,
            _clock.UtcNow,
            cancellationToken);
    }

    private async Task<IReadOnlyCollection<Guid>?> ResolveAllowedStoreIdsAsync(
        CancellationToken cancellationToken)
    {
        var role = _currentUserContext.Role
            ?? throw new UnauthorizedAppException("Authentication is required.");

        var userId = _currentUserContext.UserId
            ?? throw new UnauthorizedAppException("Authentication is required.");

        if (role == UserRole.ApplicationAdmin)
        {
            return null;
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            var stores = await _storeRepository.ListByUserMembershipAsync(userId, cancellationToken);

            return stores.Select(store => store.Id).ToArray();
        }

        throw new ForbiddenAppException("User is not allowed to access seller dashboard.");
    }
}
