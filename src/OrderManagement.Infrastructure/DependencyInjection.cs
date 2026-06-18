using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Dashboard;
using OrderManagement.Application.Abstractions.Files;
using OrderManagement.Application.Abstractions.Idempotency;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Infrastructure.ActivityLogs;
using OrderManagement.Infrastructure.Database;
using OrderManagement.Infrastructure.Files;
using OrderManagement.Infrastructure.Idempotency;
using OrderManagement.Infrastructure.Options;
using OrderManagement.Infrastructure.Repositories;
using OrderManagement.Infrastructure.Rules;
using OrderManagement.Infrastructure.Security;
using OrderManagement.Infrastructure.Time;

namespace OrderManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.Configure<MigrationOptions>(
            configuration.GetSection(MigrationOptions.SectionName));

        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));

        services.Configure<IdempotencyOptions>(
            configuration.GetSection(IdempotencyOptions.SectionName));

        services.Configure<ActivityLogOptions>(
            configuration.GetSection(ActivityLogOptions.SectionName));

        services.Configure<FileUploadOptions>(
            configuration.GetSection(FileUploadOptions.SectionName));


        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        services.AddScoped<IDatabaseMigrationRunner, DatabaseMigrationRunner>();

        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductManagementRepository, ProductManagementRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<IStoreRepository, StoreRepository>(); // Added for store functionality
        services.AddScoped<IBackofficeDashboardRepository, BackofficeDashboardRepository>();
        services.AddScoped<IBackofficeOrderRepository, BackofficeOrderRepository>();


        services.AddSingleton<IRequestHashService, RequestHashService>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        services.AddSingleton<IOrderRulesService, NRulesOrderRulesService>();

        services.AddSingleton<IActivityLogQueue, BoundedChannelActivityLogQueue>();
        services.AddScoped<IActivityLogWriter, ActivityLogWriter>();

        services.AddHostedService<ActivityLogBackgroundWorker>();

        return services;
    }
}