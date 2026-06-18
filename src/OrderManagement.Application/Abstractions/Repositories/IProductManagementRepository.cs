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

    Task<AdjustProductStockResult> AdjustStockAsync(
        AdjustProductStockPersistenceRequest request,
        CancellationToken cancellationToken = default);
}