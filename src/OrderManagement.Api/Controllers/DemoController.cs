using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Api.Controllers;

/// <summary>
/// DEDICATED CONTROLLER FOR PRESENTATION / DEMO ONLY.
/// This controller bypasses JWT Authorization and simulates the logged-in user
/// based on the Request Body to easily demonstrate Concurrency and Idempotency 
/// without needing Postman Authentication Tokens.
/// </summary>
[ApiController]
[Route("api/v1/demo")]
[AllowAnonymous]
public class DemoController : ControllerBase
{
    private readonly IOrderService _orderService;

    public DemoController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost("orders")]
    public async Task<ActionResult> CreateOrder(
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromBody] DemoCreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Simulate Authenticated User (Bypass JWT)
        SimulateUser(request.RequestedByUserId, request.RequestedByRole);

        // 2. Map to existing Command
        var command = new CreateOrderCommand
        {
            CustomerId = request.CustomerId,
            Items = request.Items.Select(i => new CreateOrderItemCommand
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity
            }).ToArray(),
            ShippingAddress = request.ShippingAddress,
            IdempotencyKey = idempotencyKey,
            Endpoint = "POST /api/v1/demo/orders"
        };

        // 3. Execute Real Business Logic
        var result = await _orderService.CreateAsync(command, cancellationToken);

        if (result.IsStoredResponse)
        {
            return StatusCode(result.StatusCode, result.StoredResponseBody);
        }

        return StatusCode(201, result.Response);
    }

    [HttpPatch("orders/{id:guid}/status")]
    public async Task<ActionResult> UpdateOrderStatus(
        Guid id,
        [FromBody] DemoUpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Simulate Authenticated User (Bypass JWT)
        SimulateUser(request.RequestedByUserId, request.RequestedByRole);

        // 2. Map to existing Command
        var command = new UpdateOrderStatusCommand
        {
            OrderId = id,
            TargetStatus = request.TargetStatus,
            ExpectedRowVersion = request.ExpectedRowVersion,
            Reason = request.Reason
        };

        // 3. Execute Real Business Logic
        var result = await _orderService.UpdateStatusAsync(command, cancellationToken);

        return Ok(result);
    }

    private void SimulateUser(Guid userId, string role)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "DemoAuth");
        HttpContext.User = new ClaimsPrincipal(identity);
    }
}

public class DemoCreateOrderRequest
{
    public Guid RequestedByUserId { get; set; }
    public string RequestedByRole { get; set; } = "Buyer";
    
    public Guid CustomerId { get; set; }
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public string ShippingAddress { get; set; } = string.Empty;
}

public class DemoUpdateOrderStatusRequest
{
    public Guid RequestedByUserId { get; set; }
    public string RequestedByRole { get; set; } = "ApplicationAdmin";
    
    public string TargetStatus { get; set; } = string.Empty;
    public long ExpectedRowVersion { get; set; }
    public string? Reason { get; set; }
}
