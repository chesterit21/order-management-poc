using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class ProductService(
    IProductRepository productRepository,
    ICurrentUserContext currentUserContext,
    IClock clock,
    IValidator<ProductListQueryDto> listValidator,
    ILogger<ProductService> logger) : IProductService
{
    private readonly IProductRepository _productRepository = productRepository;
    private readonly ICurrentUserContext _currentUserContext = currentUserContext;
    private readonly IClock _clock = clock;
    private readonly IValidator<ProductListQueryDto> _listValidator = listValidator;
    private readonly ILogger<ProductService> _logger = logger;

    public async Task<PagedResult<ProductDto>> ListAsync(
        ProductListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = new ProductListQueryDto
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Page = query.Page,
            PageSize = query.PageSize
        };

        var validationResult = await _listValidator.ValidateAsync(normalizedQuery, cancellationToken);

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

        var product = await _productRepository.GetDetailByIdAsync(id, cancellationToken);

        if (product is null)
        {
            throw NotFoundAppException.Product(id);
        }

        return product;
    }
}
