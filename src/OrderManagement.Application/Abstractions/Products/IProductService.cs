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