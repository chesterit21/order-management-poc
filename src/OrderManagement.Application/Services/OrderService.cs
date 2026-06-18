using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.ValueObjects;

namespace OrderManagement.Application.Services;

public sealed class OrderService(
    IOrderRepository orderRepository,
    ICurrentUserContext currentUserContext,
    IRequestHashService requestHashService,
    IIdempotencyService idempotencyService,
    IClock clock,
    IValidator<CreateOrderCommand> createValidator,
    IValidator<ListOrdersQueryDto> listValidator,
    IValidator<UpdateOrderStatusCommand> updateStatusValidator,
    IValidator<CancelOrderCommand> cancelValidator,
    IOrderCancellationPolicy orderCancellationPolicy,
    ILogger<OrderService> logger,
    IActivityLogWriter activityLogWriter) : IOrderService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOrderRepository _orderRepository = orderRepository;
    private readonly ICurrentUserContext _currentUserContext = currentUserContext;
    private readonly IRequestHashService _requestHashService = requestHashService;
    private readonly IIdempotencyService _idempotencyService = idempotencyService;
    private readonly IClock _clock = clock;
    private readonly IValidator<CreateOrderCommand> _createValidator = createValidator;
    private readonly IValidator<ListOrdersQueryDto> _listValidator = listValidator;
    private readonly IValidator<UpdateOrderStatusCommand> _updateStatusValidator = updateStatusValidator;
    private readonly IValidator<CancelOrderCommand> _cancelValidator = cancelValidator;
    private readonly IOrderCancellationPolicy _orderCancellationPolicy = orderCancellationPolicy;
    private readonly ILogger<OrderService> _logger = logger;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;

    public async Task<CreateOrderOperationResult> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _createValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Create order request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = _currentUserContext.Role;

        if (currentRole == UserRole.SellerOperator)
        {
            throw new ForbiddenAppException("Seller operator cannot create buyer order.");
        }

        if (currentRole == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot create order.");
        }

        if (currentRole is UserRole.Buyer or UserRole.SellerAdmin &&
            command.CustomerId != currentUserId)
        {
            throw new ForbiddenAppException("Buyer can only create order for themselves.");
        }

        var requestHash = _requestHashService.ComputeHash(new
        {
            command.CustomerId,
            Items = command.Items
                .OrderBy(item => item.ProductId)
                .Select(item => new
                {
                    item.ProductId,
                    item.Quantity
                })
                .ToArray(),
            ShippingAddress = command.ShippingAddress.Trim()
        });

        var idempotencyResult = await _idempotencyService.BeginAsync(
            command.IdempotencyKey,
            currentUserId,
            command.Endpoint,
            requestHash,
            cancellationToken);

        if (idempotencyResult.HasStoredResponse)
        {
            return CreateOrderOperationResult.Stored(
                idempotencyResult.StoredStatusCode!.Value,
                idempotencyResult.StoredResponseBody!);
        }

        var orderId = Guid.NewGuid();
        var now = _clock.UtcNow;

        try
        {
            var createResult = await _orderRepository.CreateAsync(
                new CreateOrderPersistenceRequest
                {
                    OrderId = orderId,
                    CustomerId = command.CustomerId,
                    CreatedBy = currentUserId,
                    ShippingAddress = command.ShippingAddress.Trim(),
                    Items = command.Items
                        .Select(item => new CreateOrderPersistenceItem
                        {
                            ProductId = item.ProductId,
                            Quantity = item.Quantity
                        })
                        .ToArray(),
                    Now = now
                },
                cancellationToken);

            var responseBody = JsonSerializer.Serialize(createResult, JsonOptions);

            await _idempotencyService.MarkCompletedAsync(
                idempotencyResult.RecordId!.Value,
                201,
                responseBody,
                "Order",
                createResult.Id,
                cancellationToken);

            _logger.LogInformation(
                "Order created successfully. OrderId={OrderId} OrderNumber={OrderNumber} CustomerId={CustomerId}",
                createResult.Id,
                createResult.OrderNumber,
                createResult.CustomerId);

            return CreateOrderOperationResult.Created(createResult);
        }
        catch (AppException exception)
        {
            var errorBody = JsonSerializer.Serialize(
                new
                {
                    error = new
                    {
                        code = exception.Code,
                        message = exception.Message,
                        details = exception.Details
                    }
                },
                JsonOptions);

            if (idempotencyResult.RecordId is not null)
            {
                await _idempotencyService.MarkFailedAsync(
                    idempotencyResult.RecordId.Value,
                    exception.StatusCode,
                    errorBody,
                    cancellationToken);
            }

            throw;
        }
    }

    public async Task<GetOrderResult> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationAppException(
                "Order id validation failed.",
                [AppErrorDetail.ForField("id", "Order id is required.")]);
        }

        var order = await _orderRepository.GetByIdAsync(id, cancellationToken);

        if (order is null)
        {
            throw NotFoundAppException.Order(id);
        }

        EnsureCanAccessOrder(order.CustomerId);

        return order;
    }

    public async Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new ListOrdersQueryDto
        {
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
                "Order list query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        var securedQuery = currentRole is UserRole.Buyer or UserRole.SellerAdmin
            ? new ListOrdersQueryDto
            {
                CustomerId = currentUserId,
                Page = normalizedQuery.Page,
                PageSize = normalizedQuery.PageSize,
                Status = normalizedQuery.Status,
                FromDate = normalizedQuery.FromDate,
                ToDate = normalizedQuery.ToDate
            }
            : currentRole == UserRole.ApplicationAdmin
                ? normalizedQuery
                : throw new ForbiddenAppException("You do not have permission to list buyer orders.");

        _logger.LogDebug(
            "Listing orders. Status={Status} CustomerId={CustomerId} FromDate={FromDate} ToDate={ToDate} Page={Page} PageSize={PageSize}",
            securedQuery.Status,
            securedQuery.CustomerId,
            securedQuery.FromDate,
            securedQuery.ToDate,
            securedQuery.Page,
            securedQuery.PageSize);

        return await _orderRepository.ListAsync(securedQuery, null, cancellationToken);
    }

    public async Task<UpdateOrderStatusResult> UpdateStatusAsync(
        UpdateOrderStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _updateStatusValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Update order status request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        _activityLogWriter.TryWrite(
            ActivityLogTypes.OrderStatusChangeStarted,
            orderId: command.OrderId,
            metadata: new
            {
                targetStatus = command.TargetStatus,
                expectedRowVersion = command.ExpectedRowVersion,
                requestedBy = currentUserId,
                requestedByRole = currentRole
            });

        if (currentRole != UserRole.ApplicationAdmin)
        {
            throw new ForbiddenAppException("Only Application Admin can update order status from this endpoint. Seller users must use backoffice order endpoint.");
        }

        var targetStatus = Enum.Parse<OrderStatus>(command.TargetStatus, ignoreCase: true);

        if (targetStatus == OrderStatus.Cancelled)
        {
            throw new BusinessRuleAppException(
                ErrorCodes.Order_CannotUpdateToCancelledStatus,
                "Use cancel endpoint to cancel an order so stock and audit trail are handled correctly.");
        }

        var result = await _orderRepository.UpdateStatusAsync(
            new UpdateOrderStatusPersistenceRequest
            {
                OrderId = command.OrderId,
                TargetStatus = targetStatus,
                ExpectedRowVersion = command.ExpectedRowVersion,
                UpdatedBy = currentUserId,
                UpdatedByRole = currentRole,
                Reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim(),
                Now = _clock.UtcNow
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.OrderStatusChangeCompleted,
            orderId: result.Id,
            metadata: new
            {
                previousStatus = result.PreviousStatus,
                currentStatus = result.CurrentStatus,
                updatedBy = currentUserId
            });

        _logger.LogInformation(
            "Order status updated. OrderId={OrderId} PreviousStatus={PreviousStatus} CurrentStatus={CurrentStatus} UpdatedBy={UpdatedBy}",
            result.Id,
            result.PreviousStatus,
            result.CurrentStatus,
            currentUserId);

        return result;
    }

    public async Task<CancelOrderResult> CancelAsync(
        CancelOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _cancelValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Cancel order request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        // Authorization: verify order ownership BEFORE entering the transactional path.
        // This check lives in the application service (not the repository) to respect
        // clean architecture — authorization rules belong in the application/domain layer.
        // The repository's CancelAsync no longer performs this check.
        //
        // customer_id is immutable after order creation, so the TOCTOU window between
        // this read and the subsequent mutation is harmless: at worst the mutation
        // fails with 404 (order deleted) or 409 (row_version changed).
        var ownership = await _orderRepository.GetOrderOwnershipAsync(
            command.OrderId,
            cancellationToken);

        if (ownership is null)
        {
            throw NotFoundAppException.Order(command.OrderId);
        }

        EnsureCanAccessOrder(ownership.CustomerId);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.OrderCancelStarted,
            orderId: command.OrderId,
            orderNumber: ownership.OrderNumber,
            metadata: new
            {
                expectedRowVersion = command.ExpectedRowVersion,
                cancellationReason = command.CancellationReason,
                cancelledBy = currentUserId,
                cancelledByRole = currentRole.ToString()
            });

        var cancellationDecision = _orderCancellationPolicy.Resolve(
            command.CancellationReason,
            command.Reason,
            currentRole,
            isBuyerInitiated: currentRole == UserRole.Buyer);

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

        _activityLogWriter.TryWrite(
            ActivityLogTypes.OrderCancelCompleted,
            orderId: result.Id,
            metadata: new
            {
                previousStatus = result.PreviousStatus,
                cancellationReason = result.CancellationReason,
                cancelledBy = currentUserId,
                restoreStock = result.StockRestoreApplied
            });

        _logger.LogInformation(
            "Order cancelled. OrderId={OrderId} PreviousStatus={PreviousStatus} CancelledBy={CancelledBy} CancellationReason={CancellationReason} RestoreStock={RestoreStock}",
            result.Id,
            result.PreviousStatus,
            currentUserId,
            result.CancellationReason,
            result.StockRestoreApplied);

        return result;
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

    private (Guid UserId, UserRole Role) GetCurrentUserContext()
    {
        var userId = GetRequiredCurrentUserId();
        var role = GetRequiredCurrentUserRole();

        return (userId, role);
    }

    private void EnsureCanAccessOrder(Guid customerId)
    {
        if (_currentUserContext.Role == UserRole.ApplicationAdmin)
        {
            return;
        }

        if (_currentUserContext.Role is UserRole.Buyer or UserRole.SellerAdmin &&
            _currentUserContext.UserId == customerId)
        {
            return;
        }

        throw new ForbiddenAppException("You do not have permission to access this order.");
    }
}
