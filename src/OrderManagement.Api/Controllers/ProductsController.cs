using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Contracts.Products;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.DTOs.Products;

namespace OrderManagement.Api.Controllers;

[ApiController]
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
                    StoreId = product.StoreId,
                    StoreName = product.StoreName,
                    Sku = product.Sku,
                    Name = product.Name,
                    Description = product.Description,
                    ImageUrl = product.ImageUrl,
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
    }
}