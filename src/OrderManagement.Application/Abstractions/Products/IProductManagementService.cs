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

    Task<AdjustProductStockResult> AdjustStockAsync(
        AdjustProductStockCommand command,
        CancellationToken cancellationToken = default);
}