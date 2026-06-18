using System;
using System.Collections.Generic;
using FluentValidation.TestHelper;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.Validators.Orders;
using Xunit;

namespace OrderManagement.Tests.Application.Validators;

public sealed class CreateOrderCommandValidatorTests
{
    private readonly CreateOrderCommandValidator _validator = new();

    private CreateOrderCommand CreateValidCommand() => new()
    {
        IdempotencyKey = Guid.NewGuid().ToString(),
        Endpoint = "/api/v1/orders",
        CustomerId = Guid.NewGuid(),
        ShippingAddress = "123 Main St",
        Items = new List<CreateOrderItemCommand>
        {
            new() { ProductId = Guid.NewGuid(), Quantity = 1 }
        }
    };

    [Fact]
    public void Validate_WhenValid_ShouldNotHaveAnyErrors()
    {
        var command = CreateValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WhenIdempotencyKeyIsEmpty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { IdempotencyKey = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey).WithErrorMessage("Idempotency key is required.");
    }

    [Fact]
    public void Validate_WhenEndpointIsEmpty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { Endpoint = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Endpoint).WithErrorMessage("Endpoint is required.");
    }

    [Fact]
    public void Validate_WhenCustomerIdIsEmpty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { CustomerId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.CustomerId).WithErrorMessage("Customer id is required.");
    }

    [Fact]
    public void Validate_WhenShippingAddressIsEmpty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { ShippingAddress = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ShippingAddress).WithErrorMessage("Shipping address is required.");
    }

    [Fact]
    public void Validate_WhenItemsIsEmpty_ShouldHaveError()
    {
        var command = CreateValidCommand() with { Items = new List<CreateOrderItemCommand>() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Items).WithErrorMessage("Order must contain at least one item.");
    }

    [Fact]
    public void Validate_WhenItemsHasDuplicateProducts_ShouldHaveError()
    {
        var productId = Guid.NewGuid();
        var command = CreateValidCommand() with 
        { 
            Items = new List<CreateOrderItemCommand>
            {
                new() { ProductId = productId, Quantity = 1 },
                new() { ProductId = productId, Quantity = 2 }
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Items).WithErrorMessage("Duplicate product id is not allowed. Aggregate quantity per product before submitting.");
    }

    [Fact]
    public void Validate_WhenItemProductIdIsEmpty_ShouldHaveError()
    {
        var command = CreateValidCommand() with 
        { 
            Items = new List<CreateOrderItemCommand>
            {
                new() { ProductId = Guid.Empty, Quantity = 1 }
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Items[0].ProductId").WithErrorMessage("Product id is required.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenItemQuantityIsInvalid_ShouldHaveError(int quantity)
    {
        var command = CreateValidCommand() with 
        { 
            Items = new List<CreateOrderItemCommand>
            {
                new() { ProductId = Guid.NewGuid(), Quantity = quantity }
            }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("Items[0].Quantity").WithErrorMessage("Quantity must be greater than zero.");
    }
}
