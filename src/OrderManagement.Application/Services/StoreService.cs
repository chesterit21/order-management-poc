using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Stores;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed partial class StoreService(
    IStoreRepository storeRepository,
    IStoreAuthorizationService storeAuthorizationService,
    ICurrentUserContext currentUserContext,
    IClock clock,
    IValidator<OpenStoreCommand> openValidator,
    IValidator<UpdateStoreCommand> updateValidator,
    IActivityLogWriter activityLogWriter,
    ILogger<StoreService> logger) : IStoreService
{
    private readonly IStoreRepository _storeRepository = storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService = storeAuthorizationService;
    private readonly ICurrentUserContext _currentUserContext = currentUserContext;
    private readonly IClock _clock = clock;
    private readonly IValidator<OpenStoreCommand> _openValidator = openValidator;
    private readonly IValidator<UpdateStoreCommand> _updateValidator = updateValidator;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;
    private readonly ILogger<StoreService> _logger = logger;

    public async Task<StoreDto> OpenStoreAsync(
        OpenStoreCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _openValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Open store request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role is not (UserRole.Buyer or UserRole.SellerAdmin))
        {
            throw new ForbiddenAppException("Only Buyer can open a store.");
        }

        if (await _storeRepository.UserHasOwnedStoreAsync(userId, cancellationToken))
        {
            throw new ConflictAppException(
                ErrorCodes.StoreAlreadyExists,
                "User already has a store.");
        }

        var now = _clock.UtcNow;

        var store = await _storeRepository.OpenStoreAsync(
            new OpenStorePersistenceRequest
            {
                StoreId = Guid.NewGuid(),
                OwnerUserId = userId,
                StoreName = command.StoreName.Trim(),
                Slug = GenerateSlug(command.StoreName),
                Description = string.IsNullOrWhiteSpace(command.Description)
                    ? null
                    : command.Description.Trim(),
                Now = now
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.StoreCreated,
            metadata: new
            {
                storeId = store.Id,
                storeName = store.StoreName,
                ownerUserId = userId
            });

        _logger.LogInformation(
            "Store created. StoreId={StoreId} StoreName={StoreName} OwnerUserId={OwnerUserId}",
            store.Id,
            store.StoreName,
            userId);

        return store;
    }

    public async Task<IReadOnlyCollection<StoreDto>> GetMyStoresAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredUserId();
        var role = GetRequiredRole();

        if (role == UserRole.ApplicationAdmin)
        {
            return await _storeRepository.ListAllAsync(cancellationToken);
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            return await _storeRepository.ListByUserMembershipAsync(userId, cancellationToken);
        }

        return [];
    }

    public async Task<StoreDto> GetByIdAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        if (storeId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Store id validation failed.",
                [AppErrorDetail.ForField("storeId", "Store id is required.")]);
        }

        var store = await _storeRepository.GetByIdAsync(storeId, cancellationToken);

        if (store is null)
        {
            throw new NotFoundAppException(
                "Store was not found.",
                ErrorCodes.StoreNotFound,
                [AppErrorDetail.ForField("storeId", "Store id does not exist.", new { storeId })]);
        }

        await _storeAuthorizationService.EnsureCanViewStoreAsync(storeId, cancellationToken);

        return store;
    }

    public async Task<StoreDto> UpdateAsync(
        UpdateStoreCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _updateValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Update store request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        await _storeAuthorizationService.EnsureCanManageStoreAsync(
            command.StoreId,
            cancellationToken);

        var store = await _storeRepository.UpdateStoreAsync(
            new UpdateStorePersistenceRequest
            {
                StoreId = command.StoreId,
                StoreName = command.StoreName.Trim(),
                Description = string.IsNullOrWhiteSpace(command.Description)
                    ? null
                    : command.Description.Trim(),
                Now = _clock.UtcNow
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.StoreUpdated,
            metadata: new
            {
                storeId = store.Id,
                storeName = store.StoreName
            });

        return store;
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

    private static string GenerateSlug(string storeName)
    {
        var normalized = storeName.Trim().ToLowerInvariant();
        normalized = SlugUnsafeRegex().Replace(normalized, "-");
        normalized = MultipleDashRegex().Replace(normalized, "-").Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "store";
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];

        return $"{normalized}-{suffix}";
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex SlugUnsafeRegex();

    [GeneratedRegex("-+", RegexOptions.Compiled)]
    private static partial Regex MultipleDashRegex();
}
