using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Files;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products.Backoffice;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class ProductManagementService(
    IProductManagementRepository repository,
    IStoreRepository storeRepository,
    IStoreAuthorizationService storeAuthorizationService,
    ICurrentUserContext currentUserContext,
    IClock clock,
    IFileStorageService fileStorageService,
    IValidator<BackofficeProductListQueryDto> listValidator,
    IValidator<CreateProductCommand> createValidator,
    IValidator<UpdateProductCommand> updateValidator,
    IValidator<SetProductStatusCommand> statusValidator,
    IValidator<AdjustProductStockCommand> stockAdjustmentValidator,
    IActivityLogWriter activityLogWriter,
    ILogger<ProductManagementService> logger) : IProductManagementService
{
    private readonly IProductManagementRepository _repository = repository;
    private readonly IStoreRepository _storeRepository = storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService = storeAuthorizationService;
    private readonly ICurrentUserContext _currentUserContext = currentUserContext;
    private readonly IClock _clock = clock;
    private readonly IFileStorageService _fileStorageService = fileStorageService;
    private readonly IValidator<BackofficeProductListQueryDto> _listValidator = listValidator;
    private readonly IValidator<CreateProductCommand> _createValidator = createValidator;
    private readonly IValidator<UpdateProductCommand> _updateValidator = updateValidator;
    private readonly IValidator<SetProductStatusCommand> _statusValidator = statusValidator;
    private readonly IValidator<AdjustProductStockCommand> _stockAdjustmentValidator = stockAdjustmentValidator;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;
    private readonly ILogger<ProductManagementService> _logger = logger;

    public async Task<PagedResult<BackofficeProductDto>> ListAsync(
        BackofficeProductListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new BackofficeProductListQueryDto
        {
            StoreId = query.StoreId,
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            IsActive = query.IsActive,
            Page = query.Page,
            PageSize = query.PageSize
        };

        var validationResult = await _listValidator.ValidateAsync(normalizedQuery, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Backoffice product list query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var allowedStoreIds = await ResolveAllowedStoreIdsAsync(cancellationToken);

        if (normalizedQuery.StoreId is not null)
        {
            await _storeAuthorizationService.EnsureCanOperateStoreAsync(
                normalizedQuery.StoreId.Value,
                cancellationToken);
        }

        return await _repository.ListAsync(
            normalizedQuery,
            allowedStoreIds,
            cancellationToken);
    }

    public async Task<BackofficeProductDto> GetByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Product id validation failed.",
                [AppErrorDetail.ForField("productId", "Product id is required.")]);
        }

        var product = await _repository.GetByIdAsync(productId, cancellationToken);

        if (product is null)
        {
            throw NotFoundAppException.Product(productId);
        }

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            product.StoreId,
            cancellationToken);

        return product;
    }

    public async Task<BackofficeProductDto> CreateAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _createValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Create product request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            command.StoreId,
            cancellationToken);

        var sku = command.Sku.Trim().ToUpperInvariant();

        if (await _repository.SkuExistsAsync(sku, cancellationToken))
        {
            throw new ConflictAppException(
                "PRODUCT_SKU_ALREADY_EXISTS",
                "Product SKU already exists.");
        }

        var product = await _repository.CreateAsync(
            new CreateProductPersistenceRequest
            {
                ProductId = Guid.NewGuid(),
                StoreId = command.StoreId,
                Sku = sku,
                Name = command.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(command.Description)
                    ? null
                    : command.Description.Trim(),
                StockQuantity = command.StockQuantity,
                Price = command.Price,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.ProductCreated,
            orderId: null,
            productId: product.Id,
            afterState: new
            {
                product.Sku,
                product.Name,
                product.StockQuantity,
                product.Price,
                product.IsActive
            },
            metadata: new
            {
                product.StoreId,
                product.StoreName
            });

        _logger.LogInformation(
            "Product created. ProductId={ProductId} StoreId={StoreId} Sku={Sku}",
            product.Id,
            product.StoreId,
            product.Sku);

        return product;
    }

    public async Task<BackofficeProductDto> UpdateAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _updateValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Update product request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var existing = await GetByIdAsync(command.ProductId, cancellationToken);

        var product = await _repository.UpdateAsync(
            new UpdateProductPersistenceRequest
            {
                ProductId = command.ProductId,
                Name = command.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(command.Description)
                    ? null
                    : command.Description.Trim(),
                Price = command.Price,
                ExpectedRowVersion = command.ExpectedRowVersion,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.ProductUpdated,
            productId: product.Id,
            beforeState: new
            {
                existing.Name,
                existing.Description,
                existing.Price,
                existing.RowVersion
            },
            afterState: new
            {
                product.Name,
                product.Description,
                product.Price,
                product.RowVersion
            },
            metadata: new
            {
                product.StoreId,
                product.StoreName,
                product.Sku
            });

        return product;
    }

    public async Task<BackofficeProductDto> SetStatusAsync(
        SetProductStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _statusValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Set product status request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var existing = await GetByIdAsync(command.ProductId, cancellationToken);

        var product = await _repository.SetStatusAsync(
            new SetProductStatusPersistenceRequest
            {
                ProductId = command.ProductId,
                IsActive = command.IsActive,
                ExpectedRowVersion = command.ExpectedRowVersion,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            command.IsActive
                ? ActivityLogTypes.ProductActivated
                : ActivityLogTypes.ProductDeactivated,
            productId: product.Id,
            beforeState: new
            {
                existing.IsActive,
                existing.RowVersion
            },
            afterState: new
            {
                product.IsActive,
                product.RowVersion
            },
            metadata: new
            {
                product.StoreId,
                product.StoreName,
                product.Sku
            });

        return product;
    }

    public async Task<AdjustProductStockResult> AdjustStockAsync(
        AdjustProductStockCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _stockAdjustmentValidator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Adjust product stock request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var existing = await GetByIdAsync(command.ProductId, cancellationToken);

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            existing.StoreId,
            cancellationToken);

        var currentUserId = _currentUserContext.UserId
            ?? throw new UnauthorizedAppException("Authentication is required.");

        var adjustmentType = Enum.Parse<StockAdjustmentType>(
            command.AdjustmentType,
            ignoreCase: true);

        var result = await _repository.AdjustStockAsync(
            new AdjustProductStockPersistenceRequest
            {
                ProductId = command.ProductId,
                AdjustmentType = adjustmentType,
                Quantity = command.Quantity,
                ExpectedRowVersion = command.ExpectedRowVersion,
                Reason = string.IsNullOrWhiteSpace(command.Reason)
                    ? null
                    : command.Reason.Trim(),
                AdjustedBy = currentUserId,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.ProductStockAdjusted,
            productId: result.ProductId,
            beforeState: new
            {
                stockQuantity = result.StockBefore,
                rowVersion = command.ExpectedRowVersion
            },
            afterState: new
            {
                stockQuantity = result.StockAfter,
                rowVersion = result.RowVersion
            },
            metadata: new
            {
                result.StoreId,
                result.Sku,
                result.Name,
                result.AdjustmentType,
                result.Quantity,
                command.Reason,
                adjustedBy = currentUserId
            });

        _logger.LogInformation(
            "Product stock adjusted. ProductId={ProductId} StoreId={StoreId} AdjustmentType={AdjustmentType} Quantity={Quantity} StockBefore={StockBefore} StockAfter={StockAfter}",
            result.ProductId,
            result.StoreId,
            result.AdjustmentType,
            result.Quantity,
            result.StockBefore,
            result.StockAfter);

        return result;
    }

    public async Task<UploadProductImageResult> UploadImageAsync(
        UploadProductImageCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.ProductId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Upload product image request validation failed.",
                [AppErrorDetail.ForField("productId", "Product id is required.")]);
        }

        if (string.IsNullOrWhiteSpace(command.FileName))
        {
            throw new ValidationAppException(
                "Upload product image request validation failed.",
                [AppErrorDetail.ForField("file", "File name is required.")]);
        }

        if (command.Content is null)
        {
            throw new ValidationAppException(
                "Upload product image request validation failed.",
                [AppErrorDetail.ForField("file", "File is required.")]);
        }

        var existing = await GetByIdAsync(command.ProductId, cancellationToken);

        await _storeAuthorizationService.EnsureCanOperateStoreAsync(
            existing.StoreId,
            cancellationToken);

        var storedFile = await _fileStorageService.SaveProductImageAsync(
            command.ProductId,
            command.FileName,
            command.ContentType,
            command.Content,
            command.SizeBytes,
            cancellationToken);

        var updated = await _repository.UpdateImageAsync(
            new UpdateProductImagePersistenceRequest
            {
                ProductId = command.ProductId,
                ImageUrl = storedFile.PublicUrl,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _activityLogWriter.TryWrite(
            ActivityLogTypes.ProductImageUploaded,
            productId: updated.Id,
            beforeState: new
            {
                imageUrl = existing.ImageUrl,
                rowVersion = existing.RowVersion
            },
            afterState: new
            {
                imageUrl = updated.ImageUrl,
                rowVersion = updated.RowVersion
            },
            metadata: new
            {
                updated.StoreId,
                updated.StoreName,
                updated.Sku,
                storedFile.StoredFileName,
                storedFile.ContentType,
                storedFile.SizeBytes
            });

        _logger.LogInformation(
            "Product image uploaded. ProductId={ProductId} StoreId={StoreId} ImageUrl={ImageUrl}",
            updated.Id,
            updated.StoreId,
            updated.ImageUrl);

        return new UploadProductImageResult
        {
            ProductId = updated.Id,
            StoreId = updated.StoreId,
            ImageUrl = updated.ImageUrl!,
            RowVersion = updated.RowVersion,
            UpdatedAt = updated.UpdatedAt
        };
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

            return stores
                .Select(store => store.Id)
                .ToArray();
        }

        throw new ForbiddenAppException("User is not allowed to manage products.");
    }
}
