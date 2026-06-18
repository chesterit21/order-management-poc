using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Auth;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.DTOs.Auth;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(
            new LoginCommand
            {
                Username = request.Username,
                Password = request.Password
            },
            cancellationToken);

        return Ok(new LoginResponse
        {
            AccessToken = result.AccessToken,
            ExpiresIn = result.ExpiresIn,
            User = new AuthenticatedUserResponse
            {
                Id = result.User.Id,
                Username = result.User.Username,
                DisplayName = result.User.DisplayName,
                Role = result.User.Role
            }
        });
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(cancellationToken);

        return Ok(new
        {
            message = "Logout successful."
        });
    }
}