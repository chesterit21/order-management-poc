using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
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

    [HttpPost("{id:guid}/stock/adjust")]
    [ProducesResponseType(typeof(AdjustProductStockResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdjustProductStockResponse>> AdjustStock(
        Guid id,
        [FromBody] AdjustProductStockRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _productManagementService.AdjustStockAsync(
            new AdjustProductStockCommand
            {
                ProductId = id,
                AdjustmentType = request.AdjustmentType,
                Quantity = request.Quantity,
                ExpectedRowVersion = request.ExpectedRowVersion,
                Reason = request.Reason
            },
            cancellationToken);

        return Ok(new AdjustProductStockResponse
        {
            ProductId = result.ProductId,
            StoreId = result.StoreId,
            Sku = result.Sku,
            Name = result.Name,
            AdjustmentType = Enum.Parse<OrderManagement.Domain.Enums.StockAdjustmentType>(result.AdjustmentType, ignoreCase: true),
            Quantity = result.Quantity,
            StockBefore = result.StockBefore,
            StockAfter = result.StockAfter,
            RowVersion = result.RowVersion,
            UpdatedAt = result.UpdatedAt
        });
    }

    [HttpPost("{id:guid}/image")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadProductImageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UploadProductImageResponse>> UploadImage(
        Guid id,
        [FromForm] UploadProductImageRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null)
        {
            return BadRequest(new
            {
                error = "File is required."
            });
        }

        await using var stream = request.File.OpenReadStream();

        var result = await _productManagementService.UploadImageAsync(
            new UploadProductImageCommand
            {
                ProductId = id,
                FileName = request.File.FileName,
                ContentType = request.File.ContentType,
                SizeBytes = request.File.Length,
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