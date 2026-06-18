using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Constants;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class StoreAuthorizationService : IStoreAuthorizationService
{
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IStoreRepository _storeRepository;

    public StoreAuthorizationService(
        ICurrentUserContext currentUserContext,
        IStoreRepository storeRepository)
    {
        _currentUserContext = currentUserContext;
        _storeRepository = storeRepository;
    }

    public async Task EnsureCanViewStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role == UserRole.ApplicationAdmin)
        {
            return;
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            if (await _storeRepository.IsStoreOwnerAsync(storeId, userId, cancellationToken) ||
                await _storeRepository.IsStoreOperatorAsync(storeId, userId, cancellationToken))
            {
                return;
            }
        }

        throw new ForbiddenAppException("You do not have permission to view this store.");
    }

    public async Task EnsureCanManageStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role == UserRole.ApplicationAdmin)
        {
            return;
        }

        if (role == UserRole.SellerAdmin &&
            await _storeRepository.IsStoreOwnerAsync(storeId, userId, cancellationToken))
        {
            return;
        }

        throw new ForbiddenAppException("You do not have permission to manage this store.");
    }

    public async Task EnsureCanOperateStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role == UserRole.ApplicationAdmin)
        {
            return;
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            if (await _storeRepository.IsStoreOwnerAsync(storeId, userId, cancellationToken) ||
                await _storeRepository.IsStoreOperatorAsync(storeId, userId, cancellationToken))
            {
                return;
            }
        }

        throw new ForbiddenAppException("You do not have permission to operate this store.");
    }

    private Guid GetRequiredUserId()
    {
        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId is null)
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        return _currentUserContext.UserId.Value;
    }

    private UserRole GetRequiredRole()
    {
        return _currentUserContext.Role
            ?? throw new ForbiddenAppException("User role claim is missing.");
    }
}