using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Application.Services;

public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    IClock clock,
    IValidator<LoginCommand> validator,
    ILogger<AuthService> logger,
    IActivityLogWriter activityLogWriter) : IAuthService
{
    private static readonly string DummyBcryptHash =
        "$2a$10$7EqJtq98hPqEX7fNZaFWoOHiV9TbhbcM9E4s5f8IC0LObDJ1vQ7iK";

    private readonly IUserRepository _userRepository = userRepository;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator = jwtTokenGenerator;
    private readonly IClock _clock = clock;
    private readonly IValidator<LoginCommand> _validator = validator;
    private readonly ILogger<AuthService> _logger = logger;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;

    public async Task<LoginResult> LoginAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Login request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var normalizedUsername = command.Username.Trim();

        var user = await _userRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);

        var passwordHashToVerify = user?.PasswordHash ?? DummyBcryptHash;
        var passwordValid = _passwordHasher.VerifyPassword(command.Password, passwordHashToVerify);

        if (user is null || !passwordValid)
        {
            _activityLogWriter.TryWrite(
                ActivityLogTypes.LoginFailed,
                errorCode: ErrorCodes.InvalidCredentials,
                metadata: new
                {
                    username = normalizedUsername,
                    reason = "InvalidCredentials"
                });

            _logger.LogWarning(
                "Login failed for username {Username}. Reason={Reason}",
                normalizedUsername,
                "InvalidCredentials");

            throw UnauthorizedAppException.InvalidCredentials();
        }

        if (!user.IsActive)
        {
            _activityLogWriter.TryWrite(
                ActivityLogTypes.LoginFailed,
                errorCode: ErrorCodes.InvalidCredentials,
                metadata: new
                {
                    username = normalizedUsername,
                    reason = "InactiveUser"
                });

            _logger.LogWarning(
                "Login failed for username {Username}. Reason={Reason} UserId={UserId}",
                normalizedUsername,
                "InactiveUser",
                user.Id);

            throw new UnauthorizedAppException(
                "Invalid username or password.",
                ErrorCodes.InvalidCredentials);
        }

        var generatedToken = _jwtTokenGenerator.GenerateAccessToken(user, _clock.UtcNow);

        _logger.LogInformation(
            "Login succeeded for username {Username}. UserId={UserId} Role={Role}",
            user.Username,
            user.Id,
            user.Role.ToString());

        _activityLogWriter.TryWrite(
            ActivityLogTypes.UserLoggedIn,
            metadata: new
            {
                userId = user.Id,
                username = user.Username,
                role = user.Role.ToString()
            });

        return new LoginResult
        {
            AccessToken = generatedToken.Token,
            ExpiresIn = generatedToken.ExpiresInSeconds,
            User = new AuthenticatedUserResult
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = user.Role.ToString()
            }
        };
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        // Actor info (UserId, Username, Role) is automatically populated by
        // ActivityLogWriter via the scoped HttpActivityLogContextAccessor.
        _activityLogWriter.TryWrite(
            ActivityLogTypes.UserLoggedOut,
            metadata: new
            {
                reason = "UserInitiated"
            });

        _logger.LogInformation(
            "User logged out. Activity log entry written.");

        // JWT is stateless — token invalidation requires a blacklist, which
        // is not yet implemented. The activity log entry serves as an audit trail.
        return Task.CompletedTask;
    }
}
