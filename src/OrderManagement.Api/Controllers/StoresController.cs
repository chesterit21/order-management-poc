using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Stores;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[Route("api/v1/stores")]
public sealed class StoresController : ControllerBase
{
    private readonly IStoreService _storeService;

    public StoresController(IStoreService storeService)
    {
        _storeService = storeService;
    }

    [HttpPost("open")]
    [Authorize(Policy = AuthorizationPolicies.BuyerOrSellerAdmin)]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreResponse>> OpenStore(
        [FromBody] OpenStoreRequest request,
        CancellationToken cancellationToken)
    {
        var store = await _storeService.OpenStoreAsync(
            new OpenStoreCommand
            {
                StoreName = request.StoreName,
                Description = request.Description
            },
            cancellationToken);

        return Ok(MapStore(store));
    }

    [HttpGet("my")]
    [ProducesResponseType(typeof(IReadOnlyCollection<StoreResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<StoreResponse>>> GetMyStores(
        CancellationToken cancellationToken)
    {
        var stores = await _storeService.GetMyStoresAsync(cancellationToken);

        return Ok(stores.Select(MapStore).ToArray());
    }

    [HttpGet("{storeId:guid}")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreResponse>> GetById(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var store = await _storeService.GetByIdAsync(storeId, cancellationToken);

        return Ok(MapStore(store));
    }

    [HttpPatch("{storeId:guid}")]
    [ProducesResponseType(typeof(StoreResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreResponse>> Update(
        Guid storeId,
        [FromBody] UpdateStoreRequest request,
        CancellationToken cancellationToken)
    {
        var store = await _storeService.UpdateAsync(
            new UpdateStoreCommand
            {
                StoreId = storeId,
                StoreName = request.StoreName,
                Description = request.Description
            },
            cancellationToken);

        return Ok(MapStore(store));
    }

    private static StoreResponse MapStore(StoreDto store)
    {
        return new StoreResponse
        {
            Id = store.Id,
            OwnerUserId = store.OwnerUserId,
            StoreName = store.StoreName,
            Slug = store.Slug,
            Description = store.Description,
            LogoUrl = store.LogoUrl,
            IsActive = store.IsActive,
            CreatedAt = store.CreatedAt,
            UpdatedAt = store.UpdatedAt
        };
    }
}