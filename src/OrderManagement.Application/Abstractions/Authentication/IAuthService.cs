using OrderManagement.Application.DTOs.Auth;

namespace OrderManagement.Application.Abstractions.Authentication;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}