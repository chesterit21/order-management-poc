using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Middleware;
using OrderManagement.Application.Constants;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Api.Extensions;

public static class AuthenticationExtensions
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        ValidateJwtOptions(jwtOptions);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = false;
                options.SaveToken = false;
                options.IncludeErrorDetails = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,

                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,

                    ValidateLifetime = true,
                    RequireExpirationTime = true,

                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = "username",
                    RoleClaimType = "role"
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        var correlationId = GetCorrelationId(context.HttpContext);

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = MediaTypeNames.Application.Json;
                        context.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;

                        var response = new ApiErrorResponse
                        {
                            Error = new ApiError
                            {
                                Code = ErrorCodes.Unauthorized,
                                Message = "Authentication is required or the token is invalid.",
                                Details = [],
                                CorrelationId = correlationId,
                                Timestamp = DateTimeOffset.UtcNow
                            }
                        };

                        await JsonSerializer.SerializeAsync(
                            context.Response.Body,
                            response,
                            JsonSerializerOptions);
                    },
                    OnForbidden = async context =>
                    {
                        var correlationId = GetCorrelationId(context.HttpContext);

                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = MediaTypeNames.Application.Json;
                        context.Response.Headers[CorrelationIdConstants.HeaderName] = correlationId;

                        var response = new ApiErrorResponse
                        {
                            Error = new ApiError
                            {
                                Code = ErrorCodes.Forbidden,
                                Message = "You do not have permission to access this resource.",
                                Details = [],
                                CorrelationId = correlationId,
                                Timestamp = DateTimeOffset.UtcNow
                            }
                        };

                        await JsonSerializer.SerializeAsync(
                            context.Response.Body,
                            response,
                            JsonSerializerOptions);
                    },
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtAuthentication");

                        logger.LogWarning(
                            context.Exception,
                            "JWT authentication failed. Path={Path}",
                            context.HttpContext.Request.Path.Value);

                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    private static void ValidateJwtOptions(JwtOptions jwtOptions)
    {
        if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
        {
            throw new InvalidOperationException("JWT issuer is not configured.");
        }

        if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
        {
            throw new InvalidOperationException("JWT audience is not configured.");
        }

        if (string.IsNullOrWhiteSpace(jwtOptions.Secret))
        {
            throw new InvalidOperationException("JWT secret is not configured.");
        }

        if (Encoding.UTF8.GetByteCount(jwtOptions.Secret) < 32)
        {
            throw new InvalidOperationException("JWT secret must be at least 32 bytes.");
        }

        if (jwtOptions.AccessTokenExpirationMinutes <= 0)
        {
            throw new InvalidOperationException("JWT access token expiration must be greater than zero.");
        }
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(CorrelationIdConstants.HttpContextItemName, out var value) &&
            value is string correlationId &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }

        if (context.Request.Headers.TryGetValue(CorrelationIdConstants.HeaderName, out var values))
        {
            var headerValue = values.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue.Trim();
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}