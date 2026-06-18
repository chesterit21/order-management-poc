Gaskeun bro. Ini **Batch 5: Products API + Dapper Repository**.

Target setelah batch ini jalan:

```http
GET /api/v1/products?page=1&pageSize=20&search=mouse
GET /api/v1/products/{id}
```

Kita bikin production-grade-ish:

```text
- Authenticated endpoint
- Pagination bounded
- Search parameterized
- Dapper explicit SQL aliases
- No SQL injection risk
- Consistent NotFound exception
- Application service clean
- Repository hanya data access
- Controller tipis
- DTO mapping eksplisit
```

***

# Batch 5 — Products API + Dapper Repository

## 1. Application Common DTO

### `src/OrderManagement.Application/DTOs/Common/PagedResult.cs`

Replace isi file:

```csharp
namespace OrderManagement.Application.DTOs.Common;

public sealed class PagedResult<T>
{
    public IReadOnlyCollection<T> Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public long TotalItems { get; init; }

    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalItems / (double)PageSize);
}
```

***

# 2. Product DTOs

## 2.1 `ProductListQueryDto.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Products/ProductListQueryDto.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Products;

public sealed class ProductListQueryDto
{
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
```

***

## 2.2 `ProductDto.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Products/ProductDto.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Products;

public sealed class ProductDto
{
    public required Guid Id { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required int StockQuantity { get; init; }

    public required decimal Price { get; init; }

    public required long RowVersion { get; init; }

    public required bool IsActive { get; init; }
}
```

***

# 3. Product Query Validator

Create folder kalau belum ada:

```bash
mkdir -p src/OrderManagement.Application/Validators/Products
```

Create file:

```text
src/OrderManagement.Application/Validators/Products/ProductListQueryDtoValidator.cs
```

Isi:

```csharp
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
```

***

# 4. Product Service Abstraction

Create file:

```text
src/OrderManagement.Application/Abstractions/Products/IProductService.cs
```

Kalau folder belum ada:

```bash
mkdir -p src/OrderManagement.Application/Abstractions/Products
```

Isi:

```csharp
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products;

namespace OrderManagement.Application.Abstractions.Products;

public interface IProductService
{
    Task<PagedResult<ProductDto>> ListAsync(
        ProductListQueryDto query,
        CancellationToken cancellationToken = default);

    Task<ProductDto> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
```

***

# 5. Product Repository Abstraction

## `src/OrderManagement.Application/Abstractions/Repositories/IProductRepository.cs`

Replace isi file:

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

    Task<Product?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
```

> Untuk list kita langsung return read-model `ProductDto` agar efisien dan tidak perlu instantiate domain entity untuk query listing. Untuk `GetById`, kita return domain entity karena detail product nanti bisa dipakai business flow order.

***

# 6. Product Service

## `src/OrderManagement.Application/Services/ProductService.cs`

Replace isi file:

```csharp
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Application.Services;

public sealed class ProductService : IProductService
{
    private readonly IProductRepository _productRepository;
    private readonly IValidator<ProductListQueryDto> _listQueryValidator;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository productRepository,
        IValidator<ProductListQueryDto> listQueryValidator,
        ILogger<ProductService> logger)
    {
        _productRepository = productRepository;
        _listQueryValidator = listQueryValidator;
        _logger = logger;
    }

    public async Task<PagedResult<ProductDto>> ListAsync(
        ProductListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new ProductListQueryDto
        {
            Search = string.IsNullOrWhiteSpace(query.Search)
                ? null
                : query.Search.Trim(),
            Page = query.Page,
            PageSize = query.PageSize
        };

        var validationResult = await _listQueryValidator.ValidateAsync(
            normalizedQuery,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Product list query validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        _logger.LogDebug(
            "Listing products. Search={Search} Page={Page} PageSize={PageSize}",
            normalizedQuery.Search,
            normalizedQuery.Page,
            normalizedQuery.PageSize);

        return await _productRepository.ListAsync(normalizedQuery, cancellationToken);
    }

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

        var product = await _productRepository.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            throw NotFoundAppException.Product(id);
        }

        return new ProductDto
        {
            Id = product.Id,
            Sku = product.Sku.Value,
            Name = product.Name,
            StockQuantity = product.StockQuantity,
            Price = product.Price.Amount,
            RowVersion = product.RowVersion,
            IsActive = product.IsActive
        };
    }
}
```

***

# 7. Application DI Update

## `src/OrderManagement.Application/DependencyInjection.cs`

Replace isi file:

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Application.Services;
using OrderManagement.Application.Validators.Auth;
using OrderManagement.Application.Validators.Products;

namespace OrderManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProductService, ProductService>();

        services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
        services.AddScoped<IValidator<ProductListQueryDto>, ProductListQueryDtoValidator>();

        return services;
    }
}
```

***

# 8. Product Repository with Dapper

## `src/OrderManagement.Infrastructure/Repositories/ProductRepository.cs`

Replace isi file:

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResult<ProductDto>> ListAsync(
        ProductListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var offset = (query.Page - 1) * query.PageSize;
        var search = NormalizeSearch(query.Search);

        var whereClause = search is null
            ? "WHERE is_active = TRUE"
            : """
              WHERE is_active = TRUE
                AND (
                    sku ILIKE @Search ESCAPE '\'
                    OR name ILIKE @Search ESCAPE '\'
                )
              """;

        var countSql = $"""
                        SELECT COUNT(*)
                        FROM products
                        {whereClause};
                        """;

        var dataSql = $"""
                       SELECT
                           id AS Id,
                           sku AS Sku,
                           name AS Name,
                           stock_quantity AS StockQuantity,
                           price AS Price,
                           row_version AS RowVersion,
                           is_active AS IsActive
                       FROM products
                       {whereClause}
                       ORDER BY name ASC, id ASC
                       LIMIT @PageSize OFFSET @Offset;
                       """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var parameters = new
        {
            Search = search,
            query.PageSize,
            Offset = offset
        };

        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                countSql,
                parameters,
                cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<ProductDto>(
            new CommandDefinition(
                dataSql,
                parameters,
                cancellationToken: cancellationToken));

        return new PagedResult<ProductDto>
        {
            Items = items.AsList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<Product?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               sku AS Sku,
                               name AS Name,
                               stock_quantity AS StockQuantity,
                               price AS Price,
                               row_version AS RowVersion,
                               is_active AS IsActive,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM products
                           WHERE id = @Id
                             AND is_active = TRUE
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<ProductRow>(
            new CommandDefinition(
                sql,
                new { Id = id },
                cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = EscapeLikePattern(search.Trim());

        return $"%{escaped}%";
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    private sealed class ProductRow
    {
        public Guid Id { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public int StockQuantity { get; init; }

        public decimal Price { get; init; }

        public long RowVersion { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public Product ToDomain()
        {
            return Product.Rehydrate(
                Id,
                Sku,
                Name,
                StockQuantity,
                Price,
                RowVersion,
                IsActive,
                CreatedAt,
                UpdatedAt);
        }
    }
}
```

## Kenapa search pakai escape?

Karena `%` dan `_` di SQL `LIKE/ILIKE` adalah wildcard. Dengan escape ini, input user diperlakukan lebih literal dan tetap parameterized.

***

# 9. Infrastructure DI Update

## `src/OrderManagement.Infrastructure/DependencyInjection.cs`

Replace isi file:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Infrastructure.Database;
using OrderManagement.Infrastructure.Options;
using OrderManagement.Infrastructure.Repositories;
using OrderManagement.Infrastructure.Security;
using OrderManagement.Infrastructure.Time;

namespace OrderManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.Configure<MigrationOptions>(
            configuration.GetSection(MigrationOptions.SectionName));

        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));

        services.Configure<IdempotencyOptions>(
            configuration.GetSection(IdempotencyOptions.SectionName));

        services.AddHttpContextAccessor();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        services.AddScoped<IDatabaseMigrationRunner, DatabaseMigrationRunner>();

        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();

        return services;
    }
}
```

***

# 10. API Product Contracts

## 10.1 `ProductListItemResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Products/ProductListItemResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Products;

public sealed class ProductListItemResponse
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public bool IsActive { get; init; }
}
```

***

## 10.2 `ProductDetailResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Products/ProductDetailResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Products;

public sealed class ProductDetailResponse
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public long RowVersion { get; init; }

    public bool IsActive { get; init; }
}
```

***

## 10.3 `ProductListQuery.cs`

Create file:

```text
src/OrderManagement.Api/Contracts/Products/ProductListQuery.cs
```

Isi:

```csharp
namespace OrderManagement.Api.Contracts.Products;

public sealed class ProductListQuery
{
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
```

***

# 11. ProductsController

## `src/OrderManagement.Api/Controllers/ProductsController.cs`

Replace isi file:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Contracts.Products;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.DTOs.Products;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[Route("api/v1/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProductListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ProductListItemResponse>>> List(
        [FromQuery] ProductListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _productService.ListAsync(
            new ProductListQueryDto
            {
                Search = query.Search,
                Page = query.Page,
                PageSize = query.PageSize
            },
            cancellationToken);

        return Ok(new PagedResponse<ProductListItemResponse>
        {
            Items = result.Items
                .Select(product => new ProductListItemResponse
                {
                    Id = product.Id,
                    Sku = product.Sku,
                    Name = product.Name,
                    StockQuantity = product.StockQuantity,
                    Price = product.Price,
                    IsActive = product.IsActive
                })
                .ToArray(),
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
    [ProducesResponseType(typeof(ProductDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var product = await _productService.GetByIdAsync(id, cancellationToken);

        return Ok(new ProductDetailResponse
        {
            Id = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            StockQuantity = product.StockQuantity,
            Price = product.Price,
            RowVersion = product.RowVersion,
            IsActive = product.IsActive
        });
    }
}
```

***

# 12. Seed Products

Kalau belum seed product:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -f db/seed/002_seed_products.sql
```

Cek:

```bash
PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -c "SELECT id, sku, name, stock_quantity, price FROM products ORDER BY name;"
```

***

# 13. Build

Run:

```bash
dotnet build
```

Kalau sukses:

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

***

# 14. Test Login Ambil Token

```bash
TOKEN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Password123!"}' \
  | jq -r '.accessToken')
```

Kalau API lu jalan di HTTP:

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Password123!"}' \
  | jq -r '.accessToken')
```

Cek token:

```bash
echo "$TOKEN"
```

***

# 15. Test Product List

HTTPS:

```bash
curl -k -i "https://localhost:7000/api/v1/products?page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Correlation-ID: test-products-001"
```

HTTP:

```bash
curl -i "http://localhost:5000/api/v1/products?page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Correlation-ID: test-products-001"
```

Expected response:

```json
{
  "items": [
    {
      "id": "uuid",
      "sku": "PRD-HEADSET-001",
      "name": "Gaming Headset",
      "stockQuantity": 10,
      "price": 350000,
      "isActive": true
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 3,
    "totalPages": 1
  }
}
```

***

# 16. Test Search

```bash
curl -k -i "https://localhost:7000/api/v1/products?search=mouse&page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN"
```

***

# 17. Test Product Detail

Ambil id product:

```bash
PRODUCT_ID=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT id FROM products WHERE sku = 'PRD-MOUSE-001' LIMIT 1;" \
  | xargs)
```

Hit endpoint:

```bash
curl -k -i "https://localhost:7000/api/v1/products/$PRODUCT_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Expected:

```json
{
  "id": "uuid",
  "sku": "PRD-MOUSE-001",
  "name": "Mouse Wireless",
  "stockQuantity": 15,
  "price": 150000,
  "rowVersion": 1,
  "isActive": true
}
```

***

# 18. Test Unauthorized

Tanpa token:

```bash
curl -k -i "https://localhost:7000/api/v1/products"
```

Expected:

```json
{
  "error": {
    "code": "UNAUTHORIZED",
    "message": "Authentication is required or the token is invalid.",
    "details": [],
    "correlationId": "...",
    "timestamp": "..."
  }
}
```

***

# 19. Test Validation

Invalid page size:

```bash
curl -k -i "https://localhost:7000/api/v1/products?page=0&pageSize=1000" \
  -H "Authorization: Bearer $TOKEN"
```

Expected:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Product list query validation failed.",
    "details": [
      {
        "field": "Page",
        "message": "Page must be greater than or equal to 1.",
        "metadata": null
      },
      {
        "field": "PageSize",
        "message": "Page size must be between 1 and 100.",
        "metadata": null
      }
    ],
    "correlationId": "...",
    "timestamp": "..."
  }
}
```

***

# 20. Production Notes untuk Presentasi

Dengan Batch 5 ini, Product API sudah punya beberapa guard penting:

```text
1. Endpoint protected JWT.
2. Pagination dibatasi max 100 untuk mencegah query berat.
3. Search menggunakan parameterized query.
4. LIKE wildcard user di-escape.
5. List query pakai read-model DTO untuk performance.
6. Detail query map ke domain entity.
7. Error 404 konsisten via NotFoundAppException.
8. Semua request tetap punya correlation ID dan structured logs.
```

***

# 21. Commit Batch 5

```bash
git add .
git commit -m "feat: add products API and Dapper repository"
```

***
