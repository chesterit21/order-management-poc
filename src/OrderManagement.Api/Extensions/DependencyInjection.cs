using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Api.Security;
using OrderManagement.Application.Abstractions.Authentication;

namespace OrderManagement.Api.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();

        return services;
    }
}