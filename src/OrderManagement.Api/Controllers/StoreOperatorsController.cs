using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Stores;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StoreBackofficeUser)]
[Route("api/v1/stores/{storeId:guid}/operators")]
public sealed class StoreOperatorsController : ControllerBase
{
    private readonly IStoreOperatorService _storeOperatorService;

    public StoreOperatorsController(IStoreOperatorService storeOperatorService)
    {
        _storeOperatorService = storeOperatorService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<StoreMemberResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<StoreMemberResponse>>> List(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var operators = await _storeOperatorService.ListOperatorsAsync(
            storeId,
            cancellationToken);

        return Ok(operators.Select(MapMember).ToArray());
    }

    [HttpPost]
    [ProducesResponseType(typeof(StoreMemberResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreMemberResponse>> Create(
        Guid storeId,
        [FromBody] CreateStoreOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var member = await _storeOperatorService.CreateOperatorAsync(
            new CreateStoreOperatorCommand
            {
                StoreId = storeId,
                Username = request.Username,
                Password = request.Password,
                DisplayName = request.DisplayName
            },
            cancellationToken);

        return Ok(MapMember(member));
    }

    [HttpPatch("{operatorUserId:guid}/status")]
    [ProducesResponseType(typeof(StoreMemberResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreMemberResponse>> SetStatus(
        Guid storeId,
        Guid operatorUserId,
        [FromBody] SetStoreOperatorStatusRequest request,
        CancellationToken cancellationToken)
    {
        var member = await _storeOperatorService.SetOperatorStatusAsync(
            new SetStoreOperatorStatusCommand
            {
                StoreId = storeId,
                OperatorUserId = operatorUserId,
                IsActive = request.IsActive
            },
            cancellationToken);

        return Ok(MapMember(member));
    }

    private static StoreMemberResponse MapMember(StoreMemberDto member)
    {
        return new StoreMemberResponse
        {
            Id = member.Id,
            StoreId = member.StoreId,
            UserId = member.UserId,
            Username = member.Username,
            DisplayName = member.DisplayName,
            Role = member.Role,
            IsActive = member.IsActive,
            CreatedAt = member.CreatedAt,
            UpdatedAt = member.UpdatedAt
        };
    }
}