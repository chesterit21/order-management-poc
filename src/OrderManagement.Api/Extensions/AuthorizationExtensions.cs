using OrderManagement.Domain.Enums;

namespace OrderManagement.Api.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.AuthenticatedUser,
                policy => policy.RequireAuthenticatedUser());

            options.AddPolicy(
                AuthorizationPolicies.BuyerOnly,
                policy => policy.RequireRole(UserRole.Buyer.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.BuyerOrSellerAdmin,
                policy => policy.RequireRole(
                    UserRole.Buyer.ToString(),
                    UserRole.SellerAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.SellerAdminOnly,
                policy => policy.RequireRole(UserRole.SellerAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.SellerOperatorOnly,
                policy => policy.RequireRole(UserRole.SellerOperator.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.SellerAdminOrOperator,
                policy => policy.RequireRole(
                    UserRole.SellerAdmin.ToString(),
                    UserRole.SellerOperator.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.ApplicationAdminOnly,
                policy => policy.RequireRole(UserRole.ApplicationAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.DevOpsOnly,
                policy => policy.RequireRole(UserRole.DevOps.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.ApplicationAdminOrDevOps,
                policy => policy.RequireRole(
                    UserRole.ApplicationAdmin.ToString(),
                    UserRole.DevOps.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.StoreBackofficeUser,
                policy => policy.RequireRole(
                    UserRole.SellerAdmin.ToString(),
                    UserRole.SellerOperator.ToString(),
                    UserRole.ApplicationAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.InternalUser,
                policy => policy.RequireRole(
                    UserRole.ApplicationAdmin.ToString(),
                    UserRole.DevOps.ToString()));

            // Legacy policy names.
            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(UserRole.ApplicationAdmin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.OpsOnly,
                policy => policy.RequireRole(UserRole.DevOps.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.AdminOrOps,
                policy => policy.RequireRole(
                    UserRole.ApplicationAdmin.ToString(),
                    UserRole.DevOps.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.CustomerOnly,
                policy => policy.RequireRole(UserRole.Buyer.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.CustomerOrAdmin,
                policy => policy.RequireRole(
                    UserRole.Buyer.ToString(),
                    UserRole.SellerAdmin.ToString(),
                    UserRole.ApplicationAdmin.ToString()));
        });

        return services;
    }
}