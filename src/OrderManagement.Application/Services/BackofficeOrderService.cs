using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class BackofficeOrderService(
    IBackofficeOrderRepository backofficeOrderRepository,
    IOrderRepository orderRepository,
    IStoreRepository storeRepository,
    IStoreAuthorizationService storeAuthorizationService,
    IOrderCancellationPolicy orderCancellationPolicy,
    ICurrentUserContext currentUserContext,
    IClock clock,
    IValidator<BackofficeOrderListQueryDto> listValidator,
    IValidator<BackofficeUpdateOrderStatusCommand> updateStatusValidator,
    IValidator<BackofficeCancelOrderCommand> cancelValidator,
    IActivityLogWriter activityLogWriter,
    ILogger<BackofficeOrderService> logger) : IBackofficeOrderService
{
    private readonly IBackofficeOrderRepository _backofficeOrderRepository = backofficeOrderRepository;
    private readonly IOrderRepository _orderRepository = orderRepository;
    private readonly IStoreRepository _storeRepository = storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService = storeAuthorizationService;
    private readonly IOrderCancellationPolicy _orderCancellationPolicy = orderCancellationPolicy;
    private readonly ICurrentUserContext _currentUserContext = currentUserContext;
    private readonly IClock _clock = clock;
    private readonly IValidator<BackofficeOrderListQueryDto> _listValidator = listValidator;
    private readonly IValidator<BackofficeUpdateOrderStatusCommand> _updateStatusValidator = updateStatusValidator;
    private readonly IValidator<BackofficeCancelOrderCommand> _cancelValidator = cancelValidator;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;
    private readonly ILogger<BackofficeOrderService> _logger = logger;

    public async Task<PagedResult<BackofficeOrderListItemDto>> ListAsync(
        BackofficeOrderListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new BackofficeOrderListQueryDto
        {
            StoreId = query.StoreId,
            Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim(),
            CustomerId = query.CustomerId,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            Page = query.Page,
            PageSize = query.PageSize
        };

        var validationResult = await _listValidator.ValidateAsync(normalizedQuery, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice order list query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        if (normalizedQuery.StoreId is not null)
        {
            await _storeAuthorizationService.EnsureCanOperateStoreAsync(
                normalizedQuery.StoreId.Value,
                cancellationToken);
        }

        var allowedStoreIds = await ResolveAllowedStoreIdsAsync(cancellationToken);

        return await _backofficeOrderRepository.ListAsync(
            normalizedQuery,
            allowedStoreIds,
            cancellationToken);
    }

    public async Task<BackofficeOrderDetailDto> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Order id validation failed.",
                [AppErrorDetail.ForField("orderId", "Order id is required.")]);
        }

        var order = await _backofficeOrderRepository.GetDetailByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw NotFoundAppException.Order(orderId);
        }

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            order.StoreId,
            cancellationToken);

        return order;
    }

    public async Task<UpdateOrderStatusResult> UpdateStatusAsync(
        BackofficeUpdateOrderStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _updateStatusValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice update order status request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var access = await GetRequiredAccessAsync(command.OrderId, cancellationToken);

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            access.StoreId,
            cancellationToken);

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        if (currentRole == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot update business order status.");
        }

        var targetStatus = Enum.Parse<OrderStatus>(
            command.TargetStatus,
            ignoreCase: true);

        if (targetStatus == OrderStatus.Cancelled)
        {
            throw new BusinessRuleAppException(
                ErrorCodes.CancelledStatusRequiresCancelEndpoint,
                "Use cancel endpoint to cancel an order so stock and audit trail are handled correctly.");
        }

        _activityLogWriter.TryWrite(
            ActivityLogTypes.OrderStatusChangeStarted,
            orderId: command.OrderId,
            metadata: new
            {
                access.StoreId,
                targetStatus = targetStatus.ToString(),
                expectedRowVersion = command.ExpectedRowVersion,
                requestedBy = currentUserId,
                requestedByRole = currentRole.ToString(),
                source = "Backoffice"
            });

        var result = await _orderRepository.UpdateStatusAsync(
            new UpdateOrderStatusPersistenceRequest
            {
                OrderId = command.OrderId,
                TargetStatus = targetStatus,
                ExpectedRowVersion = command.ExpectedRowVersion,
                UpdatedBy = currentUserId,
                UpdatedByRole = currentRole,
                Reason = string.IsNullOrWhiteSpace(command.Reason)
                    ? null
                    : command.Reason.Trim(),
                Now = _clock.UtcNow
            },
            cancellationToken);

        _logger.LogInformation(
            "Backoffice order status updated. OrderId={OrderId} StoreId={StoreId} PreviousStatus={PreviousStatus} CurrentStatus={CurrentStatus} UpdatedBy={UpdatedBy}",
            result.Id,
            access.StoreId,
            result.PreviousStatus,
            result.CurrentStatus,
            currentUserId);

        return result;
    }

    public async Task<CancelOrderResult> CancelAsync(
        BackofficeCancelOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _cancelValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice cancel order request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var access = await GetRequiredAccessAsync(command.OrderId, cancellationToken);

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            access.StoreId,
            cancellationToken);

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        if (currentRole == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot cancel business order.");
        }

        var cancellationDecision = _orderCancellationPolicy.Resolve(
            command.CancellationReason,
            command.Reason,
            currentRole,
            isBuyerInitiated: false);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.OrderCancelStarted,
            orderId: command.OrderId,
            metadata: new
            {
                access.StoreId,
                cancellationReason = cancellationDecision.CancellationReason.ToString(),
                restoreStock = cancellationDecision.RestoreStock,
                requestedBy = currentUserId,
                requestedByRole = currentRole.ToString(),
                source = "Backoffice"
            });

        var result = await _orderRepository.CancelAsync(
            new CancelOrderPersistenceRequest
            {
                OrderId = command.OrderId,
                ExpectedRowVersion = command.ExpectedRowVersion,
                CancelledBy = currentUserId,
                CancelledByRole = currentRole,
                CancellationReason = cancellationDecision.CancellationReason,
                RestoreStock = cancellationDecision.RestoreStock,
                Reason = cancellationDecision.ReasonText,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _logger.LogInformation(
            "Backoffice order cancelled. OrderId={OrderId} StoreId={StoreId} PreviousStatus={PreviousStatus} CancelledBy={CancelledBy}",
            result.Id,
            access.StoreId,
            result.PreviousStatus,
            currentUserId);

        return result;
    }

    private async Task<BackofficeOrderAccessDto> GetRequiredAccessAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var access = await _backofficeOrderRepository.GetAccessAsync(orderId, cancellationToken);

        if (access is null)
        {
            throw NotFoundAppException.Order(orderId);
        }

        return access;
    }

    private async Task<IReadOnlyCollection<Guid>?> ResolveAllowedStoreIdsAsync(
        CancellationToken cancellationToken)
    {
        var role = GetRequiredCurrentUserRole();
        var userId = GetRequiredCurrentUserId();

        if (role == UserRole.ApplicationAdmin)
        {
            return null;
        }

        if (role == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot access business orders.");
        }

        if (role is UserRole.SellerAdmin or UserRole.SellerOperator)
        {
            var stores = await _storeRepository.ListByUserMembershipAsync(userId, cancellationToken);

            return stores
                .Select(store => store.Id)
                .ToArray();
        }

        throw new ForbiddenAppException("User is not allowed to access backoffice orders.");
    }

    private Guid GetRequiredCurrentUserId()
    {
        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId is null)
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        return _currentUserContext.UserId.Value;
    }

    private UserRole GetRequiredCurrentUserRole()
    {
        return _currentUserContext.Role
            ?? throw new ForbiddenAppException("User role claim is missing.");
    }
}
