Gas sayangku, lanjut **Batch 16B — Product Management by Store/Seller**.

Batch ini akan menutup gap besar yang kemarin kita temukan:

```text
- Product harus belong to Store
- SellerAdmin/SellerOperator manage product berdasarkan store membership
- ApplicationAdmin bisa manage semua product
- Buyer hanya lihat public active products
- Product list/detail public include image/description/store
- Activity log untuk product audit
```

Endpoint baru:

```http
GET   /api/v1/backoffice/products
POST  /api/v1/backoffice/products
GET   /api/v1/backoffice/products/{id}
PATCH /api/v1/backoffice/products/{id}
PATCH /api/v1/backoffice/products/{id}/status
```

> Upload image belum di batch ini ya bro. Batch 16C khusus image upload. Di 16B kita siapin column `primary_image_url` dan response-nya.

***

# Batch 16B — Product Management by Store/Seller

***

## 1. Migration: Update Products for Store Ownership

Create file:

```text
db/migrations/015_update_products_for_store_ownership.sql
```

Isi:

```sql
ALTER TABLE products
ADD COLUMN IF NOT EXISTS store_id UUID NULL REFERENCES stores(id);

ALTER TABLE products
ADD COLUMN IF NOT EXISTS description TEXT NULL;

ALTER TABLE products
ADD COLUMN IF NOT EXISTS primary_image_url TEXT NULL;

CREATE INDEX IF NOT EXISTS idx_products_store_id
ON products(store_id);

CREATE INDEX IF NOT EXISTS idx_products_store_active
ON products(store_id, is_active);

CREATE INDEX IF NOT EXISTS idx_products_created_at
ON products(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_products_name
ON products(name);
```

## Kenapa `store_id` nullable dulu?

Karena produk seed lama mungkin sudah ada sebelum konsep store. Dengan nullable migration, DB existing tidak gagal startup.

Untuk data baru dari backoffice:

```text
store_id wajib dari application validator.
```

Nanti kalau semua data sudah migrated, bisa buat migration hardening:

```sql
ALTER TABLE products ALTER COLUMN store_id SET NOT NULL;
```

***

# 2. ActivityLogTypes Update

File:

```text
src/OrderManagement.Application/DTOs/ActivityLogs/ActivityLogTypes.cs
```

Tambahkan:

```csharp
public const string ProductCreated = "ProductCreated";
public const string ProductUpdated = "ProductUpdated";
public const string ProductActivated = "ProductActivated";
public const string ProductDeactivated = "ProductDeactivated";
public const string ProductImageUploaded = "ProductImageUploaded";
public const string ProductStockAdjusted = "ProductStockAdjusted";
```

> `ProductImageUploaded` dan `ProductStockAdjusted` dipakai batch 16C/16D, tapi kita define sekarang.

***

# 3. Update Public Product DTO

## `src/OrderManagement.Application/DTOs/Products/ProductDto.cs`

Replace:

```csharp
namespace OrderManagement.Application.DTOs.Products;

public sealed class ProductDto
{
    public required Guid Id { get; init; }

    public Guid? StoreId { get; init; }

    public string? StoreName { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public required int StockQuantity { get; init; }

    public required decimal Price { get; init; }

    public required long RowVersion { get; init; }

    public required bool IsActive { get; init; }
}
```

***

# 4. Backoffice Product DTOs

Create folder:

```text
src/OrderManagement.Application/DTOs/Products/Backoffice
```

***

## 4.1 `BackofficeProductDto.cs`

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class BackofficeProductDto
{
    public required Guid Id { get; init; }

    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public required int StockQuantity { get; init; }

    public required decimal Price { get; init; }

    public required long RowVersion { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 4.2 `BackofficeProductListQueryDto.cs`

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class BackofficeProductListQueryDto
{
    public Guid? StoreId { get; init; }

    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
```

***

## 4.3 `CreateProductCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class CreateProductCommand
{
    public required Guid StoreId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required int StockQuantity { get; init; }

    public required decimal Price { get; init; }
}
```

***

## 4.4 `UpdateProductCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class UpdateProductCommand
{
    public required Guid ProductId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal Price { get; init; }

    public required long ExpectedRowVersion { get; init; }
}
```

***

## 4.5 `SetProductStatusCommand.cs`

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class SetProductStatusCommand
{
    public required Guid ProductId { get; init; }

    public required bool IsActive { get; init; }

    public required long ExpectedRowVersion { get; init; }
}
```

***

## 4.6 `ProductManagementPersistenceRequests.cs`

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class CreateProductPersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required Guid StoreId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required int StockQuantity { get; init; }

    public required decimal Price { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed class UpdateProductPersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal Price { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed class SetProductStatusPersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required bool IsActive { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public required DateTimeOffset Now { get; init; }
}
```

***

# 5. Product Management Abstractions

## 5.1 `IProductManagementService.cs`

Create file:

```text
src/OrderManagement.Application/Abstractions/Products/IProductManagementService.cs
```

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Abstractions.Products;

public interface IProductManagementService
{
    Task<PagedResult<BackofficeProductDto>> ListAsync(
        BackofficeProductListQueryDto query,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> GetByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> CreateAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> UpdateAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> SetStatusAsync(
        SetProductStatusCommand command,
        CancellationToken cancellationToken = default);
}
```

***

## 5.2 `IProductManagementRepository.cs`

Create file:

```text
src/OrderManagement.Application/Abstractions/Repositories/IProductManagementRepository.cs
```

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IProductManagementRepository
{
    Task<PagedResult<BackofficeProductDto>> ListAsync(
        BackofficeProductListQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto?> GetByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<bool> SkuExistsAsync(
        string sku,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> CreateAsync(
        CreateProductPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> UpdateAsync(
        UpdateProductPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<BackofficeProductDto> SetStatusAsync(
        SetProductStatusPersistenceRequest request,
        CancellationToken cancellationToken = default);
}
```

***

# 6. Validators

Create folder:

```text
src/OrderManagement.Application/Validators/Products/Backoffice
```

***

## 6.1 `BackofficeProductListQueryDtoValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class BackofficeProductListQueryDtoValidator : AbstractValidator<BackofficeProductListQueryDto>
{
    public BackofficeProductListQueryDtoValidator()
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
```

***

## 6.2 `CreateProductCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(command => command.StoreId)
            .NotEmpty()
            .WithMessage("Store id is required.");

        RuleFor(command => command.Sku)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("SKU is required.")
            .MaximumLength(100)
            .WithMessage("SKU cannot be longer than 100 characters.");

        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Product name is required.")
            .MaximumLength(200)
            .WithMessage("Product name cannot be longer than 200 characters.");

        RuleFor(command => command.Description)
            .MaximumLength(2000)
            .WithMessage("Description cannot be longer than 2000 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Description));

        RuleFor(command => command.StockQuantity)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Stock quantity cannot be negative.");

        RuleFor(command => command.Price)
            .GreaterThan(0)
            .WithMessage("Price must be greater than zero.");
    }
}
```

***

## 6.3 `UpdateProductCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(command => command.ProductId)
            .NotEmpty()
            .WithMessage("Product id is required.");

        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Product name is required.")
            .MaximumLength(200)
            .WithMessage("Product name cannot be longer than 200 characters.");

        RuleFor(command => command.Description)
            .MaximumLength(2000)
            .WithMessage("Description cannot be longer than 2000 characters.")
            .When(command => !string.IsNullOrWhiteSpace(command.Description));

        RuleFor(command => command.Price)
            .GreaterThan(0)
            .WithMessage("Price must be greater than zero.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");
    }
}
```

***

## 6.4 `SetProductStatusCommandValidator.cs`

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Application.Validators.Products.Backoffice;

public sealed class SetProductStatusCommandValidator : AbstractValidator<SetProductStatusCommand>
{
    public SetProductStatusCommandValidator()
    {
        RuleFor(command => command.ProductId)
            .NotEmpty()
            .WithMessage("Product id is required.");

        RuleFor(command => command.ExpectedRowVersion)
            .GreaterThan(0)
            .WithMessage("Expected row version must be greater than zero.");
    }
}
```

***

# 7. Update Public Product Repository Contract

File:

```text
src/OrderManagement.Application/Abstractions/Repositories/IProductRepository.cs
```

Replace:

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IProductRepository
{
    Task<PagedResult<ProductDto>> ListAsync(
        ProductListQueryDto query,
        CancellationToken cancellationToken = default);

    Task<ProductDto?> GetDetailByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    // Keep for future domain usage if needed.
    Task<Product?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
```

***

# 8. Update ProductService

File:

```text
src/OrderManagement.Application/Services/ProductService.cs
```

Di method `GetByIdAsync`, replace query domain product dengan detail DTO:

```csharp
public async Task<ProductDto> GetByIdAsync(
    Guid id,
    CancellationToken cancellationToken = default)
{
    if (id == Guid.Empty)
    {
        throw new ValidationAppException(
            "Product id validation failed.",
            [AppErrorDetail.ForField("id", "Product id is required.")]);
    }

    var product = await _productRepository.GetDetailByIdAsync(id, cancellationToken);

    if (product is null)
    {
        throw NotFoundAppException.Product(id);
    }

    return product;
}
```

***

# 9. ProductManagementService

Create file:

```text
src/OrderManagement.Application/Services/ProductManagementService.cs
```

```csharp
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
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

public sealed class ProductManagementService : IProductManagementService
{
    private readonly IProductManagementRepository _repository;
    private readonly IStoreRepository _storeRepository;
    private readonly IStoreAuthorizationService _storeAuthorizationService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IClock _clock;
    private readonly IValidator<BackofficeProductListQueryDto> _listValidator;
    private readonly IValidator<CreateProductCommand> _createValidator;
    private readonly IValidator<UpdateProductCommand> _updateValidator;
    private readonly IValidator<SetProductStatusCommand> _statusValidator;
    private readonly IActivityLogWriter _activityLogWriter;
    private readonly ILogger<ProductManagementService> _logger;

    public ProductManagementService(
        IProductManagementRepository repository,
        IStoreRepository storeRepository,
        IStoreAuthorizationService storeAuthorizationService,
        ICurrentUserContext currentUserContext,
        IClock clock,
        IValidator<BackofficeProductListQueryDto> listValidator,
        IValidator<CreateProductCommand> createValidator,
        IValidator<UpdateProductCommand> updateValidator,
        IValidator<SetProductStatusCommand> statusValidator,
        IActivityLogWriter activityLogWriter,
        ILogger<ProductManagementService> logger)
    {
        _repository = repository;
        _storeRepository = storeRepository;
        _storeAuthorizationService = storeAuthorizationService;
        _currentUserContext = currentUserContext;
        _clock = clock;
        _listValidator = listValidator;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _statusValidator = statusValidator;
        _activityLogWriter = activityLogWriter;
        _logger = logger;
    }

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
```

***

# 10. ProductManagementRepository

Create file:

```text
src/OrderManagement.Infrastructure/Repositories/ProductManagementRepository.cs
```

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products.Backoffice;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class ProductManagementRepository : IProductManagementRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductManagementRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResult<BackofficeProductDto>> ListAsync(
        BackofficeProductListQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        CancellationToken cancellationToken = default)
    {
        var offset = (query.Page - 1) * query.PageSize;

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (allowedStoreIds is not null)
        {
            if (allowedStoreIds.Count == 0)
            {
                return new PagedResult<BackofficeProductDto>
                {
                    Items = [],
                    Page = query.Page,
                    PageSize = query.PageSize,
                    TotalItems = 0
                };
            }

            conditions.Add("p.store_id = ANY(@AllowedStoreIds)");
            parameters.Add("AllowedStoreIds", allowedStoreIds.ToArray());
        }

        if (query.StoreId is not null)
        {
            conditions.Add("p.store_id = @StoreId");
            parameters.Add("StoreId", query.StoreId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("""
                           (
                               p.sku ILIKE @Search ESCAPE '\'
                               OR p.name ILIKE @Search ESCAPE '\'
                           )
                           """);
            parameters.Add("Search", $"%{EscapeLikePattern(query.Search.Trim())}%");
        }

        if (query.IsActive is not null)
        {
            conditions.Add("p.is_active = @IsActive");
            parameters.Add("IsActive", query.IsActive.Value);
        }

        parameters.Add("PageSize", query.PageSize);
        parameters.Add("Offset", offset);

        var whereClause = conditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", conditions);

        var countSql = $"""
                        SELECT COUNT(*)
                        FROM products p
                        INNER JOIN stores s ON s.id = p.store_id
                        {whereClause};
                        """;

        var dataSql = $"""
                       SELECT
                           p.id AS Id,
                           p.store_id AS StoreId,
                           s.store_name AS StoreName,
                           p.sku AS Sku,
                           p.name AS Name,
                           p.description AS Description,
                           p.primary_image_url AS ImageUrl,
                           p.stock_quantity AS StockQuantity,
                           p.price AS Price,
                           p.row_version AS RowVersion,
                           p.is_active AS IsActive,
                           p.created_at AS CreatedAt,
                           p.updated_at AS UpdatedAt
                       FROM products p
                       INNER JOIN stores s ON s.id = p.store_id
                       {whereClause}
                       ORDER BY p.created_at DESC, p.id DESC
                       LIMIT @PageSize OFFSET @Offset;
                       """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                countSql,
                parameters,
                cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<BackofficeProductDto>(
            new CommandDefinition(
                dataSql,
                parameters,
                cancellationToken: cancellationToken));

        return new PagedResult<BackofficeProductDto>
        {
            Items = items.AsList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<BackofficeProductDto?> GetByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               p.id AS Id,
                               p.store_id AS StoreId,
                               s.store_name AS StoreName,
                               p.sku AS Sku,
                               p.name AS Name,
                               p.description AS Description,
                               p.primary_image_url AS ImageUrl,
                               p.stock_quantity AS StockQuantity,
                               p.price AS Price,
                               p.row_version AS RowVersion,
                               p.is_active AS IsActive,
                               p.created_at AS CreatedAt,
                               p.updated_at AS UpdatedAt
                           FROM products p
                           INNER JOIN stores s ON s.id = p.store_id
                           WHERE p.id = @ProductId
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<BackofficeProductDto>(
            new CommandDefinition(
                sql,
                new { ProductId = productId },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> SkuExistsAsync(
        string sku,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM products
                               WHERE lower(sku) = lower(@Sku)
                           );
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { Sku = sku },
                cancellationToken: cancellationToken));
    }

    public async Task<BackofficeProductDto> CreateAsync(
        CreateProductPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           INSERT INTO products
                               (id, store_id, sku, name, description, primary_image_url,
                                stock_quantity, price, row_version, is_active, created_at, updated_at)
                           VALUES
                               (@ProductId, @StoreId, @Sku, @Name, @Description, NULL,
                                @StockQuantity, @Price, 1, TRUE, @Now, @Now);
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        return await GetByIdAsync(request.ProductId, cancellationToken)
               ?? throw new InvalidOperationException("Created product cannot be found.");
    }

    public async Task<BackofficeProductDto> UpdateAsync(
        UpdateProductPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE products
                           SET
                               name = @Name,
                               description = @Description,
                               price = @Price,
                               row_version = row_version + 1,
                               updated_at = @Now
                           WHERE id = @ProductId
                             AND row_version = @ExpectedRowVersion;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw new ConcurrencyAppException(
                "Product has been modified by another user. Please refresh and try again.");
        }

        return await GetByIdAsync(request.ProductId, cancellationToken)
               ?? throw new InvalidOperationException("Updated product cannot be found.");
    }

    public async Task<BackofficeProductDto> SetStatusAsync(
        SetProductStatusPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE products
                           SET
                               is_active = @IsActive,
                               row_version = row_version + 1,
                               updated_at = @Now
                           WHERE id = @ProductId
                             AND row_version = @ExpectedRowVersion;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw new ConcurrencyAppException(
                "Product has been modified by another user. Please refresh and try again.");
        }

        return await GetByIdAsync(request.ProductId, cancellationToken)
               ?? throw new InvalidOperationException("Updated product cannot be found.");
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }
}
```

***

# 11. Update Public ProductRepository

File:

```text
src/OrderManagement.Infrastructure/Repositories/ProductRepository.cs
```

Update list query select menjadi include store/image/description:

```sql
SELECT
    p.id AS Id,
    p.store_id AS StoreId,
    s.store_name AS StoreName,
    p.sku AS Sku,
    p.name AS Name,
    p.description AS Description,
    p.primary_image_url AS ImageUrl,
    p.stock_quantity AS StockQuantity,
    p.price AS Price,
    p.row_version AS RowVersion,
    p.is_active AS IsActive
FROM products p
LEFT JOIN stores s ON s.id = p.store_id
...
```

Karena existing code pakai table direct `products`, replace alias:

```sql
WHERE p.is_active = TRUE
```

Search:

```sql
p.sku ILIKE @Search ESCAPE '\'
OR p.name ILIKE @Search ESCAPE '\'
```

Order:

```sql
ORDER BY p.name ASC, p.id ASC
```

Tambah method baru:

```csharp
public async Task<ProductDto?> GetDetailByIdAsync(
    Guid id,
    CancellationToken cancellationToken = default)
{
    const string sql = """
                       SELECT
                           p.id AS Id,
                           p.store_id AS StoreId,
                           s.store_name AS StoreName,
                           p.sku AS Sku,
                           p.name AS Name,
                           p.description AS Description,
                           p.primary_image_url AS ImageUrl,
                           p.stock_quantity AS StockQuantity,
                           p.price AS Price,
                           p.row_version AS RowVersion,
                           p.is_active AS IsActive
                       FROM products p
                       LEFT JOIN stores s ON s.id = p.store_id
                       WHERE p.id = @Id
                         AND p.is_active = TRUE
                       LIMIT 1;
                       """;

    await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

    return await connection.QuerySingleOrDefaultAsync<ProductDto>(
        new CommandDefinition(
            sql,
            new { Id = id },
            cancellationToken: cancellationToken));
}
```

> Existing `GetByIdAsync` domain method boleh tetap seperti sebelumnya untuk compatibility, tapi query-nya perlu pakai alias `p.` kalau table berubah.

***

# 12. API Contracts

Create folder:

```text
src/OrderManagement.Api/Contracts/Products/Backoffice
```

***

## 12.1 `BackofficeProductQuery.cs`

```csharp
namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed class BackofficeProductQuery
{
    public Guid? StoreId { get; init; }

    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
```

***

## 12.2 `CreateProductRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed class CreateProductRequest
{
    public Guid StoreId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }
}
```

***

## 12.3 `UpdateProductRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed class UpdateProductRequest
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public decimal Price { get; init; }

    public long ExpectedRowVersion { get; init; }
}
```

***

## 12.4 `SetProductStatusRequest.cs`

```csharp
namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed class SetProductStatusRequest
{
    public bool IsActive { get; init; }

    public long ExpectedRowVersion { get; init; }
}
```

***

## 12.5 `BackofficeProductResponse.cs`

```csharp
namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed class BackofficeProductResponse
{
    public Guid Id { get; init; }

    public Guid StoreId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public long RowVersion { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
```

***

# 13. Update Public Product Responses

## `ProductListItemResponse.cs`

Replace:

```csharp
namespace OrderManagement.Api.Contracts.Products;

public sealed class ProductListItemResponse
{
    public Guid Id { get; init; }

    public Guid? StoreId { get; init; }

    public string? StoreName { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public bool IsActive { get; init; }
}
```

***

## `ProductDetailResponse.cs`

Replace:

```csharp
namespace OrderManagement.Api.Contracts.Products;

public sealed class ProductDetailResponse
{
    public Guid Id { get; init; }

    public Guid? StoreId { get; init; }

    public string? StoreName { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public long RowVersion { get; init; }

    public bool IsActive { get; init; }
}
```

***

# 14. Update ProductsController Mapping

File:

```text
src/OrderManagement.Api/Controllers/ProductsController.cs
```

Di list mapping tambahkan:

```csharp
StoreId = product.StoreId,
StoreName = product.StoreName,
Description = product.Description,
ImageUrl = product.ImageUrl,
```

Di detail mapping tambahkan juga.

Contoh detail:

```csharp
return Ok(new ProductDetailResponse
{
    Id = product.Id,
    StoreId = product.StoreId,
    StoreName = product.StoreName,
    Sku = product.Sku,
    Name = product.Name,
    Description = product.Description,
    ImageUrl = product.ImageUrl,
    StockQuantity = product.StockQuantity,
    Price = product.Price,
    RowVersion = product.RowVersion,
    IsActive = product.IsActive
});
```

***

# 15. BackofficeProductsController

Create file:

```text
src/OrderManagement.Api/Controllers/BackofficeProductsController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Contracts.Products.Backoffice;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.DTOs.Products.Backoffice;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StoreBackofficeUser)]
[Route("api/v1/backoffice/products")]
public sealed class BackofficeProductsController : ControllerBase
{
    private readonly IProductManagementService _productManagementService;

    public BackofficeProductsController(IProductManagementService productManagementService)
    {
        _productManagementService = productManagementService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<BackofficeProductResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<BackofficeProductResponse>>> List(
        [FromQuery] BackofficeProductQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _productManagementService.ListAsync(
            new BackofficeProductListQueryDto
            {
                StoreId = query.StoreId,
                Search = query.Search,
                IsActive = query.IsActive,
                Page = query.Page,
                PageSize = query.PageSize
            },
            cancellationToken);

        return Ok(new PagedResponse<BackofficeProductResponse>
        {
            Items = result.Items.Select(MapProduct).ToArray(),
            Pagination = new PaginationResponse
            {
                Page = result.Page,
                PageSize = result.PageSize,
                TotalItems = result.TotalItems,
                TotalPages = result.TotalPages
            }
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BackofficeProductResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackofficeProductResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var product = await _productManagementService.GetByIdAsync(id, cancellationToken);

        return Ok(MapProduct(product));
    }

    [HttpPost]
    [ProducesResponseType(typeof(BackofficeProductResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackofficeProductResponse>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _productManagementService.CreateAsync(
            new CreateProductCommand
            {
                StoreId = request.StoreId,
                Sku = request.Sku,
                Name = request.Name,
                Description = request.Description,
                StockQuantity = request.StockQuantity,
                Price = request.Price
            },
            cancellationToken);

        return Ok(MapProduct(product));
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(BackofficeProductResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackofficeProductResponse>> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _productManagementService.UpdateAsync(
            new UpdateProductCommand
            {
                ProductId = id,
                Name = request.Name,
                Description = request.Description,
                Price = request.Price,
                ExpectedRowVersion = request.ExpectedRowVersion
            },
            cancellationToken);

        return Ok(MapProduct(product));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(BackofficeProductResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackofficeProductResponse>> SetStatus(
        Guid id,
        [FromBody] SetProductStatusRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _productManagementService.SetStatusAsync(
            new SetProductStatusCommand
            {
                ProductId = id,
                IsActive = request.IsActive,
                ExpectedRowVersion = request.ExpectedRowVersion
            },
            cancellationToken);

        return Ok(MapProduct(product));
    }

    private static BackofficeProductResponse MapProduct(BackofficeProductDto product)
    {
        return new BackofficeProductResponse
        {
            Id = product.Id,
            StoreId = product.StoreId,
            StoreName = product.StoreName,
            Sku = product.Sku,
            Name = product.Name,
            Description = product.Description,
            ImageUrl = product.ImageUrl,
            StockQuantity = product.StockQuantity,
            Price = product.Price,
            RowVersion = product.RowVersion,
            IsActive = product.IsActive,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };
    }
}
```

***

# 16. DI Updates

## Application DI

File:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.DTOs.Products.Backoffice;
using OrderManagement.Application.Validators.Products.Backoffice;
```

Tambahkan registration:

```csharp
services.AddScoped<IProductManagementService, ProductManagementService>();

services.AddScoped<IValidator<BackofficeProductListQueryDto>, BackofficeProductListQueryDtoValidator>();
services.AddScoped<IValidator<CreateProductCommand>, CreateProductCommandValidator>();
services.AddScoped<IValidator<UpdateProductCommand>, UpdateProductCommandValidator>();
services.AddScoped<IValidator<SetProductStatusCommand>, SetProductStatusCommandValidator>();
```

***

## Infrastructure DI

File:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Tambahkan:

```csharp
services.AddScoped<IProductManagementRepository, ProductManagementRepository>();
```

***

# 17. Optional Seed Update for Store Products

Kalau lu sudah punya seed store `seller-one-store`, update seed product supaya punya `store_id`.

Create file:

```text
db/seed/004_assign_products_to_seed_store.sql
```

```sql
WITH store AS (
    SELECT id
    FROM stores
    WHERE slug = 'seller-one-store'
    LIMIT 1
)
UPDATE products
SET
    store_id = store.id,
    description = CASE
        WHEN products.sku = 'PRD-MOUSE-001' THEN 'Wireless mouse suitable for productivity and daily use.'
        WHEN products.sku = 'PRD-KEYBOARD-001' THEN 'Mechanical keyboard for office and gaming.'
        WHEN products.sku = 'PRD-HEADSET-001' THEN 'Gaming headset with clear sound quality.'
        ELSE products.description
    END,
    primary_image_url = CASE
        WHEN products.sku = 'PRD-MOUSE-001' THEN '/uploads/products/placeholder-mouse.webp'
        WHEN products.sku = 'PRD-KEYBOARD-001' THEN '/uploads/products/placeholder-keyboard.webp'
        WHEN products.sku = 'PRD-HEADSET-001' THEN '/uploads/products/placeholder-headset.webp'
        ELSE products.primary_image_url
    END
FROM store
WHERE products.store_id IS NULL;
```

***

# 18. Build Check

Run:

```bash
dotnet build
```

Kalau ada error karena `ProductDto` required property baru, cek mapping di:

```text
ProductRepository
ProductService
ProductsController
```

Kalau ada error role/policy, cek:

```bash
grep -R "AdminOrOps\\|Customer\\|UserRole.Admin\\|UserRole.Customer\\|UserRole.Ops" -n src
```

***

# 19. Manual Test

## 19.1 Login seller admin

```bash
SELLER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"selleradmin1","password":"Password123!"}')

SELLER_TOKEN=$(echo "$SELLER_LOGIN" | jq -r '.accessToken')
```

## 19.2 Get my stores

```bash
curl -k -s https://localhost:7000/api/v1/stores/my \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq
```

Ambil store id:

```bash
STORE_ID=$(curl -k -s https://localhost:7000/api/v1/stores/my \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  | jq -r '.[0].id')
```

## 19.3 Create product

```bash
curl -k -X POST https://localhost:7000/api/v1/backoffice/products \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: product-create-001" \
  -d "{
    \"storeId\": \"$STORE_ID\",
    \"sku\": \"PRD-DEMO-001\",
    \"name\": \"Demo Product\",
    \"description\": \"Produk demo dari seller.\",
    \"stockQuantity\": 25,
    \"price\": 99000
  }" | jq
```

Expected:

```text
200 OK
Product has StoreId, StoreName, RowVersion = 1, IsActive = true
```

## 19.4 List backoffice products

```bash
curl -k -s "https://localhost:7000/api/v1/backoffice/products?storeId=$STORE_ID&page=1&pageSize=20" \
  -H "Authorization: Bearer $SELLER_TOKEN" | jq
```

## 19.5 Public product list includes image/description/store

```bash
BUYER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}')

BUYER_TOKEN=$(echo "$BUYER_LOGIN" | jq -r '.accessToken')

curl -k -s "https://localhost:7000/api/v1/products?page=1&pageSize=20" \
  -H "Authorization: Bearer $BUYER_TOKEN" | jq
```

Expected product item include:

```text
storeId
storeName
description
imageUrl
```

## 19.6 Check activity log

```bash
APPADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"appadmin","password":"Password123!"}')

APPADMIN_TOKEN=$(echo "$APPADMIN_LOGIN" | jq -r '.accessToken')

curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?correlationId=product-create-001&page=1&pageSize=20" \
  -H "Authorization: Bearer $APPADMIN_TOKEN" | jq
```

Expected:

```text
ProductCreated
RequestCompleted
```

***

# 20. Security Acceptance

Harus terjadi:

```text
SellerAdmin can create product for own store.
SellerOperator can operate assigned store.
ApplicationAdmin can manage all products.
Buyer cannot access /api/v1/backoffice/products.
DevOps cannot access product management.
Public product list only shows active products.
```

Tidak boleh terjadi:

```text
Seller can manage product from other store.
Buyer creates product.
DevOps creates product.
Public API exposes inactive products.
SQL injection through search.
Stale rowVersion update succeeds.
```

***

# 21. Commit

```bash
git add .
git commit -m "feat: add store-based product management"
```

***