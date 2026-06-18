using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Abstractions.Authentication;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Username { get; }

    string? DisplayName { get; }

    UserRole? Role { get; }

    bool IsInRole(UserRole role);

    bool IsBuyerLike();

    bool IsSellerBackoffice();

    bool IsApplicationAdmin();

    bool IsDevOps();

    bool IsApplicationAdminOrDevOps();

    // Legacy compatibility for previous batches.
    bool IsAdminOrOps();
}