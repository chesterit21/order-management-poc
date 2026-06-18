using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Payments;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Payments;
using OrderManagement.Application.DTOs.Payments;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[Route("api/v1/orders/{orderId:guid}/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreatePaymentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CreatePaymentResponse>> Create(
        Guid orderId,
        [FromBody] CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.CreateAsync(
            new CreatePaymentCommand
            {
                OrderId = orderId,
                Provider = request.Provider,
                SimulateResult = request.SimulateResult
            },
            cancellationToken);

        return Ok(new CreatePaymentResponse
        {
            PaymentId = result.PaymentId,
            OrderId = result.OrderId,
            Amount = result.Amount,
            Status = result.Status,
            OrderStatus = result.OrderStatus,
            Provider = result.Provider,
            PaymentReference = result.PaymentReference,
            CreatedAt = result.CreatedAt
        });
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaymentListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentListResponse>> List(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.ListByOrderIdAsync(
            orderId,
            cancellationToken);

        return Ok(new PaymentListResponse
        {
            OrderId = result.OrderId,
            Payments = result.Payments
                .Select(payment => new PaymentResponse
                {
                    Id = payment.Id,
                    Amount = payment.Amount,
                    Status = payment.Status,
                    Provider = payment.Provider,
                    PaymentReference = payment.PaymentReference,
                    CreatedAt = payment.CreatedAt,
                    UpdatedAt = payment.UpdatedAt
                })
                .ToArray()
        });
    }
}