using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Api.Security;

public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;

            var value =
                principal?.FindFirstValue(ClaimTypes.NameIdentifier) ??
                principal?.FindFirstValue("sub");

            return Guid.TryParse(value, out var userId)
                ? userId
                : null;
        }
    }

    public string? Username =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("username") ??
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

    public string? DisplayName =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("displayName");

    public UserRole? Role
    {
        get
        {
            var value =
                _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ??
                _httpContextAccessor.HttpContext?.User.FindFirstValue("role");

            return Enum.TryParse<UserRole>(value, ignoreCase: true, out var role)
                ? role
                : null;
        }
    }

    public bool IsInRole(UserRole role)
    {
        return Role == role;
    }

    public bool IsBuyerLike()
    {
        return Role is UserRole.Buyer or UserRole.SellerAdmin;
    }

    public bool IsSellerBackoffice()
    {
        return Role is UserRole.SellerAdmin or UserRole.SellerOperator;
    }

    public bool IsApplicationAdmin()
    {
        return Role == UserRole.ApplicationAdmin;
    }

    public bool IsDevOps()
    {
        return Role == UserRole.DevOps;
    }

    public bool IsApplicationAdminOrDevOps()
    {
        return Role is UserRole.ApplicationAdmin or UserRole.DevOps;
    }

    public bool IsAdminOrOps()
    {
        return IsApplicationAdmin();
    }
}