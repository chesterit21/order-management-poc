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

namespace OrderManagement.Application.Services;

public sealed class StoreOperatorService(
    IStoreRepository storeRepository,
    IStoreAuthorizationService storeAuthorizationService,
    IPasswordHasher passwordHasher,
    ICurrentUserContext currentUserContext,
    IClock clock,
    IValidator<CreateStoreOperatorCommand> createValidator,
    IValidator<SetStoreOperatorStatusCommand> statusValidator,
    IActivityLogWriter activityLogWriter,
    ILogger<StoreOperatorService> logger) : IStoreOperatorService
{
    private readonly IStoreRepository _storeRepository = storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService = storeAuthorizationService;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;
    private readonly ICurrentUserContext _currentUserContext = currentUserContext;
    private readonly IClock _clock = clock;
    private readonly IValidator<CreateStoreOperatorCommand> _createValidator = createValidator;
    private readonly IValidator<SetStoreOperatorStatusCommand> _statusValidator = statusValidator;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;
    private readonly ILogger<StoreOperatorService> _logger = logger;

    public async Task<IReadOnlyCollection<StoreMemberDto>> ListOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        if (storeId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Store id validation failed.",
                [AppErrorDetail.ForField("storeId", "Store id is required.")]);
        }

        await _storeAuthorizationService.EnsureCanManageStoreAsync(storeId, cancellationToken);

        return await _storeRepository.ListOperatorsAsync(storeId, cancellationToken);
    }

    public async Task<StoreMemberDto> CreateOperatorAsync(
        CreateStoreOperatorCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _createValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Create store operator request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        await _storeAuthorizationService.EnsureCanManageStoreAsync(
            command.StoreId,
            cancellationToken);

        var currentUserId = _currentUserContext.UserId
            ?? throw new UnauthorizedAppException("Authentication is required.");

        var now = _clock.UtcNow;

        var member = await _storeRepository.CreateOperatorAsync(
            new CreateStoreOperatorPersistenceRequest
            {
                StoreId = command.StoreId,
                OperatorUserId = Guid.NewGuid(),
                Username = command.Username.Trim(),
                PasswordHash = _passwordHasher.HashPassword(command.Password),
                DisplayName = command.DisplayName.Trim(),
                CreatedBy = currentUserId,
                Now = now
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.StoreOperatorCreated,
            metadata: new
            {
                storeId = command.StoreId,
                operatorUserId = member.UserId,
                operatorUsername = member.Username,
                createdBy = currentUserId
            });

        _logger.LogInformation(
            "Store operator created. StoreId={StoreId} OperatorUserId={OperatorUserId} CreatedBy={CreatedBy}",
            command.StoreId,
            member.UserId,
            currentUserId);

        return member;
    }

    public async Task<StoreMemberDto> SetOperatorStatusAsync(
        SetStoreOperatorStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _statusValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Set store operator status request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        await _storeAuthorizationService.EnsureCanManageStoreAsync(
            command.StoreId,
            cancellationToken);

        var member = await _storeRepository.SetOperatorStatusAsync(
            new SetStoreOperatorStatusPersistenceRequest
            {
                StoreId = command.StoreId,
                OperatorUserId = command.OperatorUserId,
                IsActive = command.IsActive,
                Now = _clock.UtcNow
            },
            cancellationToken);

        if (!command.IsActive)
        {
            _activityLogWriter.TryWrite(
                ActivityLogTypes.StoreOperatorDeactivated,
                metadata: new
                {
                    storeId = command.StoreId,
                    operatorUserId = command.OperatorUserId
                });
        }

        return member;
    }
}
