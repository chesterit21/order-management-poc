using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Demo;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Demo;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Application.Services;

public sealed class DemoService : IDemoService
{
    private readonly IOrderService _orderService;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<DemoService> _logger;

    public DemoService(
        IOrderService orderService,
        IProductRepository productRepository,
        ILogger<DemoService> logger)
    {
        _orderService = orderService;
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<ConcurrentStockDeductionResponse> RunConcurrentStockDeductionAsync(
        ConcurrentStockDeductionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Get initial stock before the race
        var product = await _productRepository.GetDetailByIdAsync(request.ProductId, cancellationToken);
        var initialStock = product?.StockQuantity ?? 0;

        _logger.LogInformation(
            "Starting concurrent stock deduction demo. ProductId={ProductId} InitialStock={InitialStock} QuantityEach={QuantityEach}",
            request.ProductId,
            initialStock,
            request.Quantity);

        // 2. Create 2 unique idempotency keys (simulating what a real client would do)
        var key1 = $"demo-csd-{Guid.NewGuid():N}";
        var key2 = $"demo-csd-{Guid.NewGuid():N}";

        // The endpoint stored in the idempotency record — matches the real POST /orders endpoint
        // that processes these requests.
        const string Endpoint = "POST /api/v1/orders";

        // 3. Fire 2 concurrent order creation tasks.
        //    Both target the same product with the same quantity.
        //    Layer 1 (Idempotency-Key): handled by OrderService via IdempotencyService
        //    Layer 2 (FOR UPDATE lock): handled by OrderRepository.CreateAsync
        var task1 = CreateOrderSafelyAsync(key1, Endpoint, request, cancellationToken);
        var task2 = CreateOrderSafelyAsync(key2, Endpoint, request, cancellationToken);

        var results = await Task.WhenAll(task1, task2);

        // 4. Get final stock after both requests complete
        var productAfter = await _productRepository.GetDetailByIdAsync(request.ProductId, cancellationToken);
        var finalStock = productAfter?.StockQuantity ?? 0;

        var successCount = results.Count(r => r.StatusCode == 201);
        var conflictCount = results.Count(r => r.StatusCode == 409);

        _logger.LogInformation(
            "Demo completed. Success={SuccessCount} Conflict={ConflictCount} InitialStock={InitialStock} FinalStock={FinalStock}",
            successCount,
            conflictCount,
            initialStock,
            finalStock);

        return new ConcurrentStockDeductionResponse
        {
            Scenario = $"Two concurrent orders for {request.Quantity} units each. Available stock: {initialStock}. Only one should succeed when stock is insufficient for both.",
            InitialStock = initialStock,
            QuantityEach = request.Quantity,
            Requests = results,
            FinalStock = finalStock,
            Summary = $"{successCount} succeeded, {conflictCount} rejected — stock never goes below 0."
        };
    }

    private async Task<DemoRequestResult> CreateOrderSafelyAsync(
        string idempotencyKey,
        string endpoint,
        ConcurrentStockDeductionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new CreateOrderCommand
            {
                IdempotencyKey = idempotencyKey,
                Endpoint = endpoint,
                CustomerId = request.CustomerId,
                ShippingAddress = request.ShippingAddress,
                Items =
                [
                    new CreateOrderItemCommand
                    {
                        ProductId = request.ProductId,
                        Quantity = request.Quantity
                    }
                ]
            };

            var result = await _orderService.CreateAsync(command, cancellationToken);

            if (result.IsStoredResponse)
            {
                return new DemoRequestResult
                {
                    IdempotencyKey = idempotencyKey,
                    StatusCode = result.StatusCode,
                    ErrorMessage = result.StatusCode == 201
                        ? "Idempotency replay — stored response returned."
                        : null
                };
            }

            var order = result.Response!;

            return new DemoRequestResult
            {
                IdempotencyKey = idempotencyKey,
                StatusCode = 201,
                OrderId = order.Id.ToString(),
                OrderNumber = order.OrderNumber
            };
        }
        catch (AppException ex)
        {
            return new DemoRequestResult
            {
                IdempotencyKey = idempotencyKey,
                StatusCode = ex.StatusCode,
                ErrorCode = ex.Code,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in demo concurrent order creation.");

            return new DemoRequestResult
            {
                IdempotencyKey = idempotencyKey,
                StatusCode = 500,
                ErrorCode = "UNEXPECTED_ERROR",
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }
}
