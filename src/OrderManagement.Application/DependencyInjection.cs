using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Dashboard;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Demo;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Abstractions.Payments;
using OrderManagement.Application.Abstractions.Products;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Stores;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.DTOs.Dashboard;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Orders.Backoffice;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Application.DTOs.Products.Backoffice;
using OrderManagement.Application.DTOs.Stores;
using OrderManagement.Application.Services;
using OrderManagement.Application.Validators.Auth;
using OrderManagement.Application.Validators.Dashboard;
using OrderManagement.Application.Validators.Orders;
using OrderManagement.Application.Validators.Orders.Backoffice;
using OrderManagement.Application.Validators.Payments;
using OrderManagement.Application.Validators.Products;
using OrderManagement.Application.Validators.Products.Backoffice;
using OrderManagement.Application.Validators.Stores;

namespace OrderManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductManagementService, ProductManagementService>();
        services.AddScoped<IOrderCancellationPolicy, OrderCancellationPolicy>();
        services.AddScoped<IStoreService, StoreService>();
        services.AddScoped<IStoreOperatorService, StoreOperatorService>();
        services.AddScoped<IStoreAuthorizationService, StoreAuthorizationService>();

        services.AddScoped<IBackofficeDashboardService, BackofficeDashboardService>();

        services.AddScoped<IBackofficeOrderService, BackofficeOrderService>();

        services.AddScoped<IActivityLogService, ActivityLogService>();

        services.AddScoped<IDemoService, DemoService>();

        services.AddScoped<IValidator<ProductListQueryDto>, ProductListQueryDtoValidator>();
        services.AddScoped<IValidator<BackofficeProductListQueryDto>, BackofficeProductListQueryDtoValidator>();
        services.AddScoped<IValidator<CreateProductCommand>, CreateProductCommandValidator>();
        services.AddScoped<IValidator<UpdateProductCommand>, UpdateProductCommandValidator>();
        services.AddScoped<IValidator<SetProductStatusCommand>, SetProductStatusCommandValidator>();
        services.AddScoped<IValidator<AdjustProductStockCommand>, AdjustProductStockCommandValidator>();

        services.AddScoped<IValidator<BackofficeOrderListQueryDto>, BackofficeOrderListQueryDtoValidator>();
        services.AddScoped<IValidator<BackofficeUpdateOrderStatusCommand>, BackofficeUpdateOrderStatusCommandValidator>();
        services.AddScoped<IValidator<BackofficeCancelOrderCommand>, BackofficeCancelOrderCommandValidator>();

        services.AddScoped<IValidator<BackofficeDashboardSummaryQueryDto>, BackofficeDashboardSummaryQueryDtoValidator>();

        services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();
        services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
        services.AddScoped<IValidator<CancelOrderCommand>, CancelOrderCommandValidator>();
        services.AddScoped<IValidator<UpdateOrderStatusCommand>, UpdateOrderStatusCommandValidator>();
        services.AddScoped<IValidator<ListOrdersQueryDto>, ListOrdersQueryValidator>();
        services.AddScoped<IValidator<CreatePaymentCommand>, CreatePaymentCommandValidator>();

        services.AddScoped<IValidator<OpenStoreCommand>, OpenStoreCommandValidator>();
        services.AddScoped<IValidator<UpdateStoreCommand>, UpdateStoreCommandValidator>();
        services.AddScoped<IValidator<CreateStoreOperatorCommand>, CreateStoreOperatorCommandValidator>();
        services.AddScoped<IValidator<SetStoreOperatorStatusCommand>, SetStoreOperatorStatusCommandValidator>();

        return services;
    }
}