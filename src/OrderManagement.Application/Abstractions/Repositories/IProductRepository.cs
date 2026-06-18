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