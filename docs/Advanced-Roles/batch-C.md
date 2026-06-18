Gas sayangku 😄 lanjut **Batch 16C — Product Image Upload**.

Batch ini implement:

```text
- IFileStorageService
- LocalProductImageStorageService
- FileUploadOptions
- multipart/form-data endpoint
- POST /api/v1/backoffice/products/{id}/image
- validate jpg/jpeg/png/webp
- max file size
- random safe filename
- update primary_image_url
- serve static files from wwwroot/uploads
- ProductImageUploaded activity log
```

Design production-aware:

```text
1. File disimpan local di wwwroot/uploads/products/{productId}/ untuk POC.
2. File name selalu random, tidak pakai original file name.
3. Extension dan content-type divalidasi.
4. File size dibatasi.
5. Path traversal dicegah.
6. Public URL disimpan ke products.primary_image_url.
7. Authorization tetap berdasarkan store ownership via product -> store.
8. Activity log ProductImageUploaded tercatat.
```

> Production note: untuk real production, file sebaiknya disimpan di object storage/blob storage, bukan local disk API server. Tapi untuk POC/demo lokal, local storage ini clean dan cukup.

***

# Batch 16C — Product Image Upload

***

## 1. Application DTOs

Buat folder kalau belum ada:

```text
src/OrderManagement.Application/DTOs/Files
```

***

## 1.1 `StoredFileResult.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Files/StoredFileResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Files;

public sealed class StoredFileResult
{
    public required string PublicUrl { get; init; }

    public required string StoredFileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }
}
```

***

## 1.2 `UploadProductImageCommand.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Products/Backoffice/UploadProductImageCommand.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class UploadProductImageCommand
{
    public required Guid ProductId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required Stream Content { get; init; }
}
```

***

## 1.3 `UploadProductImageResult.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Products/Backoffice/UploadProductImageResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class UploadProductImageResult
{
    public required Guid ProductId { get; init; }

    public required Guid StoreId { get; init; }

    public required string ImageUrl { get; init; }

    public required long RowVersion { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
```

***

## 1.4 `UpdateProductImagePersistenceRequest.cs`

Create file:

```text
src/OrderManagement.Application/DTOs/Products/Backoffice/UpdateProductImagePersistenceRequest.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed class UpdateProductImagePersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required string ImageUrl { get; init; }

    public required DateTimeOffset Now { get; init; }
}
```

***

# 2. File Storage Abstraction

Create folder:

```text
src/OrderManagement.Application/Abstractions/Files
```

## `IFileStorageService.cs`

Create file:

```text
src/OrderManagement.Application/Abstractions/Files/IFileStorageService.cs
```

```csharp
using OrderManagement.Application.DTOs.Files;

namespace OrderManagement.Application.Abstractions.Files;

public interface IFileStorageService
{
    Task<StoredFileResult> SaveProductImageAsync(
        Guid productId,
        string originalFileName,
        string contentType,
        Stream content,
        long sizeBytes,
        CancellationToken cancellationToken = default);
}
```

***

# 3. Product Management Interface Update

File:

```text
src/OrderManagement.Application/Abstractions/Products/IProductManagementService.cs
```

Tambahkan method:

```csharp
Task<UploadProductImageResult> UploadImageAsync(
    UploadProductImageCommand command,
    CancellationToken cancellationToken = default);
```

Full final:

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

    Task<UploadProductImageResult> UploadImageAsync(
        UploadProductImageCommand command,
        CancellationToken cancellationToken = default);
}
```

***

# 4. Product Management Repository Update

File:

```text
src/OrderManagement.Application/Abstractions/Repositories/IProductManagementRepository.cs
```

Tambahkan method:

```csharp
Task<BackofficeProductDto> UpdateImageAsync(
    UpdateProductImagePersistenceRequest request,
    CancellationToken cancellationToken = default);
```

Full final:

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

    Task<BackofficeProductDto> UpdateImageAsync(
        UpdateProductImagePersistenceRequest request,
        CancellationToken cancellationToken = default);
}
```

***

# 5. FileUploadOptions

File:

```text
src/OrderManagement.Infrastructure/Options/FileUploadOptions.cs
```

```csharp
namespace OrderManagement.Infrastructure.Options;

public sealed class FileUploadOptions
{
    public const string SectionName = "FileUpload";

    public long MaxProductImageSizeBytes { get; init; } = 2 * 1024 * 1024;

    public string ProductImageRootPath { get; init; } = "wwwroot/uploads/products";

    public string ProductImagePublicBasePath { get; init; } = "/uploads/products";

    public string[] AllowedProductImageExtensions { get; init; } =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    ];

    public string[] AllowedProductImageContentTypes { get; init; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];
}
```

***

# 6. LocalProductImageStorageService

Create folder:

```text
src/OrderManagement.Infrastructure/Files
```

## `LocalProductImageStorageService.cs`

Create file:

```text
src/OrderManagement.Infrastructure/Files/LocalProductImageStorageService.cs
```

```csharp
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.Files;
using OrderManagement.Application.DTOs.Files;
using OrderManagement.Application.Exceptions;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.Files;

public sealed class LocalProductImageStorageService : IFileStorageService
{
    private readonly FileUploadOptions _options;

    public LocalProductImageStorageService(IOptions<FileUploadOptions> options)
    {
        _options = options.Value;
    }

    public async Task<StoredFileResult> SaveProductImageAsync(
        Guid productId,
        string originalFileName,
        string contentType,
        Stream content,
        long sizeBytes,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ValidationAppException(
                "Product image validation failed.",
                [AppErrorDetail.ForField("file", "File name is required.")]);
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ValidationAppException(
                "Product image validation failed.",
                [AppErrorDetail.ForField("file", "File content type is required.")]);
        }

        if (sizeBytes <= 0)
        {
            throw new ValidationAppException(
                "Product image validation failed.",
                [AppErrorDetail.ForField("file", "File cannot be empty.")]);
        }

        if (sizeBytes > _options.MaxProductImageSizeBytes)
        {
            throw new ValidationAppException(
                "Product image validation failed.",
                [AppErrorDetail.ForField(
                    "file",
                    $"File size cannot be larger than {_options.MaxProductImageSizeBytes} bytes.",
                    new
                    {
                        maxSizeBytes = _options.MaxProductImageSizeBytes,
                        actualSizeBytes = sizeBytes
                    })]);
        }

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();

        if (!_options.AllowedProductImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationAppException(
                "Product image validation failed.",
                [AppErrorDetail.ForField(
                    "file",
                    "File extension is not allowed.",
                    new
                    {
                        allowedExtensions = _options.AllowedProductImageExtensions
                    })]);
        }

        if (!_options.AllowedProductImageContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationAppException(
                "Product image validation failed.",
                [AppErrorDetail.ForField(
                    "file",
                    "File content type is not allowed.",
                    new
                    {
                        allowedContentTypes = _options.AllowedProductImageContentTypes
                    })]);
        }

        var rootPath = Path.GetFullPath(_options.ProductImageRootPath);
        var productDirectory = Path.Combine(rootPath, productId.ToString("N"));
        Directory.CreateDirectory(productDirectory);

        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var targetPath = Path.Combine(productDirectory, storedFileName);
        var fullTargetPath = Path.GetFullPath(targetPath);

        if (!fullTargetPath.StartsWith(rootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid product image storage path.");
        }

        await using (var fileStream = new FileStream(
                         fullTargetPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 64 * 1024,
                         useAsync: true))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var publicUrl = string.Join(
            '/',
            _options.ProductImagePublicBasePath.TrimEnd('/'),
            productId.ToString("N"),
            storedFileName);

        return new StoredFileResult
        {
            PublicUrl = publicUrl,
            StoredFileName = storedFileName,
            ContentType = contentType,
            SizeBytes = sizeBytes
        };
    }
}
```

Security:

```text
- Tidak pakai original file name untuk path.
- File name random GUID.
- Extension validated.
- Content-Type validated.
- Size validated.
- Path traversal checked with GetFullPath + StartsWith.
```

***

# 7. ProductManagementRepository Update

File:

```text
src/OrderManagement.Infrastructure/Repositories/ProductManagementRepository.cs
```

Tambahkan method:

```csharp
public async Task<BackofficeProductDto> UpdateImageAsync(
    UpdateProductImagePersistenceRequest request,
    CancellationToken cancellationToken = default)
{
    const string sql = """
                       UPDATE products
                       SET
                           primary_image_url = @ImageUrl,
                           row_version = row_version + 1,
                           updated_at = @Now
                       WHERE id = @ProductId;
                       """;

    await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

    var affected = await connection.ExecuteAsync(
        new CommandDefinition(
            sql,
            request,
            cancellationToken: cancellationToken));

    if (affected != 1)
    {
        throw NotFoundAppException.Product(request.ProductId);
    }

    return await GetByIdAsync(request.ProductId, cancellationToken)
           ?? throw new InvalidOperationException("Updated product cannot be found.");
}
```

Pastikan using sudah ada:

```csharp
using OrderManagement.Application.DTOs.Products.Backoffice;
using OrderManagement.Application.Exceptions;
```

***

# 8. ProductManagementService Update

File:

```text
src/OrderManagement.Application/Services/ProductManagementService.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.Files;
```

Tambahkan field:

```csharp
private readonly IFileStorageService _fileStorageService;
```

Update constructor parameter:

```csharp
IFileStorageService fileStorageService,
```

Set field:

```csharp
_fileStorageService = fileStorageService;
```

***

## 8.1 Full Constructor Final

Pastikan constructor jadi seperti ini:

```csharp
public ProductManagementService(
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
    IActivityLogWriter activityLogWriter,
    ILogger<ProductManagementService> logger)
{
    _repository = repository;
    _storeRepository = storeRepository;
    _storeAuthorizationService = storeAuthorizationService;
    _currentUserContext = currentUserContext;
    _clock = clock;
    _fileStorageService = fileStorageService;
    _listValidator = listValidator;
    _createValidator = createValidator;
    _updateValidator = updateValidator;
    _statusValidator = statusValidator;
    _activityLogWriter = activityLogWriter;
    _logger = logger;
}
```

***

## 8.2 Add Method `UploadImageAsync`

Tambahkan di class:

```csharp
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
```

***

# 9. API Contracts

Create file:

```text
src/OrderManagement.Api/Contracts/Products/Backoffice/UploadProductImageResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed class UploadProductImageResponse
{
    public Guid ProductId { get; init; }

    public Guid StoreId { get; init; }

    public string ImageUrl { get; init; } = string.Empty;

    public long RowVersion { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
```

***

# 10. BackofficeProductsController Update

File:

```text
src/OrderManagement.Api/Controllers/BackofficeProductsController.cs
```

Tambahkan endpoint:

```csharp
[HttpPost("{id:guid}/image")]
[RequestSizeLimit(5 * 1024 * 1024)]
[ProducesResponseType(typeof(UploadProductImageResponse), StatusCodes.Status200OK)]
public async Task<ActionResult<UploadProductImageResponse>> UploadImage(
    Guid id,
    IFormFile file,
    CancellationToken cancellationToken)
{
    if (file is null)
    {
        return BadRequest(new
        {
            error = "File is required."
        });
    }

    await using var stream = file.OpenReadStream();

    var result = await _productManagementService.UploadImageAsync(
        new UploadProductImageCommand
        {
            ProductId = id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Content = stream
        },
        cancellationToken);

    return Ok(new UploadProductImageResponse
    {
        ProductId = result.ProductId,
        StoreId = result.StoreId,
        ImageUrl = result.ImageUrl,
        RowVersion = result.RowVersion,
        UpdatedAt = result.UpdatedAt
    });
}
```

Pastikan using DTO command sudah ada:

```csharp
using OrderManagement.Application.DTOs.Products.Backoffice;
```

> Note: `[RequestSizeLimit]` kita set 5MB untuk HTTP-level guard. Actual app config default 2MB di `FileUploadOptions`.

Kalau mau strict mengikuti option, bisa set global form limit nanti. Untuk POC cukup.

***

# 11. Infrastructure DI Update

File:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

Tambahkan using:

```csharp
using OrderManagement.Application.Abstractions.Files;
using OrderManagement.Infrastructure.Files;
```

Tambahkan configure:

```csharp
services.Configure<FileUploadOptions>(
    configuration.GetSection(FileUploadOptions.SectionName));
```

Tambahkan registration:

```csharp
services.AddSingleton<IFileStorageService, LocalProductImageStorageService>();
```

Contoh section final:

```csharp
services.Configure<ActivityLogOptions>(
    configuration.GetSection(ActivityLogOptions.SectionName));

services.Configure<FileUploadOptions>(
    configuration.GetSection(FileUploadOptions.SectionName));
```

dan registrations:

```csharp
services.AddSingleton<IFileStorageService, LocalProductImageStorageService>();
```

***

# 12. Appsettings Update

## 12.1 `appsettings.json`

Tambahkan:

```json
"FileUpload": {
  "MaxProductImageSizeBytes": 2097152,
  "ProductImageRootPath": "wwwroot/uploads/products",
  "ProductImagePublicBasePath": "/uploads/products",
  "AllowedProductImageExtensions": [ ".jpg", ".jpeg", ".png", ".webp" ],
  "AllowedProductImageContentTypes": [ "image/jpeg", "image/png", "image/webp" ]
}
```

***

## 12.2 `appsettings.Development.json`

Tambahkan jika mau override:

```json
"FileUpload": {
  "MaxProductImageSizeBytes": 5242880,
  "ProductImageRootPath": "wwwroot/uploads/products",
  "ProductImagePublicBasePath": "/uploads/products",
  "AllowedProductImageExtensions": [ ".jpg", ".jpeg", ".png", ".webp" ],
  "AllowedProductImageContentTypes": [ "image/jpeg", "image/png", "image/webp" ]
}
```

***

# 13. Program.cs — Serve Static Files

File:

```text
src/OrderManagement.Api/Program.cs
```

Tambahkan static files middleware sebelum auth/controllers:

```csharp
app.UseStaticFiles();
```

Recommended order:

```csharp
app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseCors("ClientApps");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
```

Dengan ini URL seperti:

```text
/uploads/products/{productId}/{fileName}.webp
```

bisa diakses publik.

***

# 14. Ensure Folder Exists

Tambahkan file placeholder supaya folder masuk git:

```text
src/OrderManagement.Api/wwwroot/uploads/products/.gitkeep
```

Kalau folder belum ada:

```bash
mkdir -p src/OrderManagement.Api/wwwroot/uploads/products
touch src/OrderManagement.Api/wwwroot/uploads/products/.gitkeep
```

> Catatan: kalau app run dari root solution, path `"wwwroot/uploads/products"` relatif ke working directory bisa beda. Lebih aman kita set config path ke API project working dir saat run. Jika `dotnet run --project src/OrderManagement.Api/...`, content root biasanya project folder. Kalau run dari executable publish, path relatif ke content root. Untuk production lebih proper pakai `IWebHostEnvironment.WebRootPath`, tapi Batch ini cukup. Kalau mau lebih robust, lihat note di bawah.

***

# 15. Optional Robust Path Using IWebHostEnvironment

Kalau mau lebih production-safe, update `LocalProductImageStorageService` inject `IWebHostEnvironment`.

Constructor:

```csharp
private readonly FileUploadOptions _options;
private readonly IWebHostEnvironment _environment;

public LocalProductImageStorageService(
    IOptions<FileUploadOptions> options,
    IWebHostEnvironment environment)
{
    _options = options.Value;
    _environment = environment;
}
```

Using:

```csharp
using Microsoft.AspNetCore.Hosting;
```

Root path helper:

```csharp
private string GetRootPath()
{
    if (Path.IsPathRooted(_options.ProductImageRootPath))
    {
        return Path.GetFullPath(_options.ProductImageRootPath);
    }

    var webRootPath = string.IsNullOrWhiteSpace(_environment.WebRootPath)
        ? Path.Combine(_environment.ContentRootPath, "wwwroot")
        : _environment.WebRootPath;

    var relative = _options.ProductImageRootPath;

    if (relative.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith("wwwroot\\", StringComparison.OrdinalIgnoreCase))
    {
        relative = relative["wwwroot".Length..].TrimStart('/', '\\');
    }

    return Path.GetFullPath(Path.Combine(webRootPath, relative));
}
```

Lalu replace:

```csharp
var rootPath = Path.GetFullPath(_options.ProductImageRootPath);
```

dengan:

```csharp
var rootPath = GetRootPath();
```

Gue rekomendasikan pakai versi robust ini.

***

# 16. Build

Run:

```bash
dotnet build
```

Potential compile issue:

## `IFormFile file` binding

Kalau Swagger/ASP.NET butuh attribute, bisa update endpoint signature:

```csharp
public async Task<ActionResult<UploadProductImageResponse>> UploadImage(
    Guid id,
    [FromForm] IFormFile file,
    CancellationToken cancellationToken)
```

Pakai final:

```csharp
[HttpPost("{id:guid}/image")]
[RequestSizeLimit(5 * 1024 * 1024)]
[Consumes("multipart/form-data")]
[ProducesResponseType(typeof(UploadProductImageResponse), StatusCodes.Status200OK)]
public async Task<ActionResult<UploadProductImageResponse>> UploadImage(
    Guid id,
    [FromForm] IFormFile file,
    CancellationToken cancellationToken)
```

***

# 17. Manual Test

## Login seller

```bash
SELLER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"selleradmin1","password":"Password123!"}')

SELLER_TOKEN=$(echo "$SELLER_LOGIN" | jq -r '.accessToken')
```

## Create/get product

```bash
PRODUCT_ID="<product-id>"
```

Atau ambil dari DB:

```bash
PRODUCT_ID=$(PGPASSWORD=order_password psql \
  -h localhost \
  -p 5432 \
  -U order_user \
  -d order_management \
  -t \
  -c "SELECT id FROM products WHERE sku = 'PRD-DEMO-001' LIMIT 1;" \
  | xargs)
```

## Upload image

```bash
curl -k -X POST "https://localhost:7000/api/v1/backoffice/products/$PRODUCT_ID/image" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -H "X-Correlation-ID: product-image-upload-001" \
  -F "file=@./sample-product.webp"
```

Expected:

```json
{
  "productId": "...",
  "storeId": "...",
  "imageUrl": "/uploads/products/{productIdN}/abc123.webp",
  "rowVersion": 2,
  "updatedAt": "..."
}
```

## Test URL

```bash
IMAGE_URL="<imageUrl-from-response>"

curl -k -I "https://localhost:7000$IMAGE_URL"
```

Expected:

```text
200 OK
content-type: image/webp or application/octet-stream depending static file provider
```

## Public product detail

```bash
BUYER_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer1","password":"Password123!"}')

BUYER_TOKEN=$(echo "$BUYER_LOGIN" | jq -r '.accessToken')

curl -k -s "https://localhost:7000/api/v1/products/$PRODUCT_ID" \
  -H "Authorization: Bearer $BUYER_TOKEN" | jq
```

Expected:

```text
imageUrl filled
description visible
storeName visible
```

## Activity log check

```bash
APPADMIN_LOGIN=$(curl -k -s -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"appadmin","password":"Password123!"}')

APPADMIN_TOKEN=$(echo "$APPADMIN_LOGIN" | jq -r '.accessToken')

curl -k -s "https://localhost:7000/api/v1/internal/activity-logs?correlationId=product-image-upload-001&page=1&pageSize=20" \
  -H "Authorization: Bearer $APPADMIN_TOKEN" | jq
```

Expected:

```text
ProductImageUploaded
RequestCompleted
```

***

# 18. Negative Tests

## Invalid extension

```bash
curl -k -X POST "https://localhost:7000/api/v1/backoffice/products/$PRODUCT_ID/image" \
  -H "Authorization: Bearer $SELLER_TOKEN" \
  -F "file=@./malware.exe"
```

Expected:

```text
422 VALIDATION_ERROR
```

## Too large file

Expected:

```text
422 VALIDATION_ERROR or 413 Payload Too Large depending request limit
```

## Buyer upload image

```bash
curl -k -X POST "https://localhost:7000/api/v1/backoffice/products/$PRODUCT_ID/image" \
  -H "Authorization: Bearer $BUYER_TOKEN" \
  -F "file=@./sample-product.webp"
```

Expected:

```text
403 FORBIDDEN
```

***

# 19. Security Acceptance

Harus:

```text
- SellerAdmin/SellerOperator can upload image only for own store product.
- ApplicationAdmin can upload for all products.
- Buyer cannot upload.
- DevOps cannot upload.
- File extension validated.
- Content-Type validated.
- File size validated.
- Stored filename random.
- Public URL does not expose original filename.
- No path traversal.
- Activity log does not store binary content.
```

Tidak boleh:

```text
- Upload .exe/.js/.html.
- Use original file name as storage path.
- Store JWT/password in activity log.
- Seller upload image to another seller product.
```

***

# 20. Commit

```bash
git add .
git commit -m "feat: add product image upload"
```

***#