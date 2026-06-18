Gaskeun bro. Ini **Batch 4: Auth/Login JWT + User Repository + Password Hashing** versi production-grade-ish untuk POC, tetap clean DDD layering dan security-conscious.

Target setelah batch ini:

```http
POST /api/v1/auth/login
```

Request:

```json
{
  "username": "admin",
  "password": "Password123!"
}
```

Response sukses:

```json
{
  "accessToken": "...",
  "expiresIn": 3600,
  "user": {
    "id": "...",
    "username": "admin",
    "displayName": "System Admin",
    "role": "Admin"
  }
}
```

***

# Batch 4 — Auth/Login JWT

## 0. Security Design Notes

Yang kita cover di batch ini:

```text
1. Password verification pakai BCrypt.
2. JWT signed pakai HMAC SHA-256.
3. JWT validate issuer, audience, signing key, lifetime.
4. ClockSkew kecil 30 detik.
5. Response unauthorized/forbidden tetap pakai ApiErrorResponse.
6. Tidak bocorkan apakah username salah atau password salah.
7. Current user context aman untuk baca user id, username, role.
8. Repository Dapper manual mapping, tidak expose SQL ke controller/service.
9. AuthController tetap tipis.
10. Role policy disiapkan untuk endpoint berikutnya.
```

> Note: seed password yang sebelumnya pakai PostgreSQL `crypt(..., gen_salt('bf'))` akan menghasilkan bcrypt-style hash. `BCrypt.Net-Next` biasanya bisa verify format bcrypt `$2a$ / $2b$`. Kalau nanti verify gagal, kita ubah seed hash ke hash dari `BCrypt.Net` secara eksplisit.

***

# 1. Application Abstractions

## 1.1 `ICurrentUserContext.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Authentication/ICurrentUserContext.cs
```

```csharp
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

    bool IsAdminOrOps();
}
```

***

## 1.2 `IJwtTokenGenerator.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Authentication/IJwtTokenGenerator.cs
```

```csharp
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions.Authentication;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(User user, DateTimeOffset now);
}
```

***

## 1.3 `IPasswordHasher.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Authentication/IPasswordHasher.cs
```

```csharp
namespace OrderManagement.Application.Abstractions.Authentication;

public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string passwordHash);
}
```

***

## 1.4 `IAuthService.cs`

Create file baru:

```text
src/OrderManagement.Application/Abstractions/Authentication/IAuthService.cs
```

```csharp
using OrderManagement.Application.DTOs.Auth;

namespace OrderManagement.Application.Abstractions.Authentication;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default);
}
```

***

## 1.5 `IUserRepository.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Repositories/IUserRepository.cs
```

```csharp
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
}
```

***

# 2. Time Abstraction

## 2.1 `IClock.cs`

Replace:

```text
src/OrderManagement.Application/Abstractions/Time/IClock.cs
```

```csharp
namespace OrderManagement.Application.Abstractions.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

***

## 2.2 `SystemClock.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Time/SystemClock.cs
```

```csharp
using OrderManagement.Application.Abstractions.Time;

namespace OrderManagement.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

***

# 3. Login DTOs

## 3.1 `LoginCommand.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Auth/LoginCommand.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Auth;

public sealed class LoginCommand
{
    public required string Username { get; init; }

    public required string Password { get; init; }
}
```

***

## 3.2 `LoginResult.cs`

Replace:

```text
src/OrderManagement.Application/DTOs/Auth/LoginResult.cs
```

```csharp
namespace OrderManagement.Application.DTOs.Auth;

public sealed class LoginResult
{
    public required string AccessToken { get; init; }

    public required int ExpiresIn { get; init; }

    public required AuthenticatedUserResult User { get; init; }
}

public sealed class AuthenticatedUserResult
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string Role { get; init; }
}
```

***

# 4. Login Validator

## `LoginCommandValidator.cs`

Replace:

```text
src/OrderManagement.Application/Validators/Auth/LoginCommandValidator.cs
```

```csharp
using FluentValidation;
using OrderManagement.Application.DTOs.Auth;

namespace OrderManagement.Application.Validators.Auth;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Username)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Username is required.")
            .MaximumLength(100)
            .WithMessage("Username cannot be longer than 100 characters.");

        RuleFor(command => command.Password)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Password is required.")
            .MaximumLength(200)
            .WithMessage("Password cannot be longer than 200 characters.");
    }
}
```

***

# 5. AuthService

## `AuthService.cs`

Replace:

```text
src/OrderManagement.Application/Services/AuthService.cs
```

```csharp
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.Exceptions;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Application.Services;

public sealed class AuthService : IAuthService
{
    private static readonly string DummyBcryptHash =
        "$2a$10$7EqJtq98hPqEX7fNZaFWoOHiV9TbhbcM9E4s5f8IC0LObDJ1vQ7iK";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IClock _clock;
    private readonly IValidator<LoginCommand> _validator;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IClock clock,
        IValidator<LoginCommand> validator,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _clock = clock;
        _validator = validator;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

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
            _logger.LogWarning(
                "Login failed for username {Username}. Reason={Reason}",
                normalizedUsername,
                "InvalidCredentials");

            throw UnauthorizedAppException.InvalidCredentials();
        }

        if (!user.IsActive)
        {
            _logger.LogWarning(
                "Login failed for username {Username}. Reason={Reason} UserId={UserId}",
                normalizedUsername,
                "InactiveUser",
                user.Id);

            throw new UnauthorizedAppException(
                "Invalid username or password.",
                ErrorCodes.InvalidCredentials);
        }

        var now = _clock.UtcNow;
        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user, now);

        _logger.LogInformation(
            "Login succeeded for username {Username}. UserId={UserId} Role={Role}",
            user.Username,
            user.Id,
            user.Role.ToString());

        return new LoginResult
        {
            AccessToken = accessToken,
            ExpiresIn = _jwtOptions.AccessTokenExpirationMinutes * 60,
            User = new AuthenticatedUserResult
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = user.Role.ToString()
            }
        };
    }
}
```

> Bro, `AuthService` sekarang reference `JwtOptions` dari Infrastructure. Ini secara strict clean architecture agak kurang ideal karena Application reference Infrastructure namespace. Kita punya 2 opsi:
>
> 1. Pindahkan `JwtOptions` ke Application.
> 2. Jangan inject options ke AuthService, cukup return expiry dari `IJwtTokenGenerator`.
>
> Untuk DDD lebih bersih, gue sarankan opsi 2. Jadi kita revisi kecil agar Application tidak tahu Infrastructure. Lanjut di bawah ini kita buat lebih clean.

***

# 5A. Revisi Clean DDD untuk Token Result

Agar Application tidak reference Infrastructure, ubah interface token generator agar return token + expiry.

## 5A.1 Replace `IJwtTokenGenerator.cs`

```csharp
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions.Authentication;

public interface IJwtTokenGenerator
{
    GeneratedAccessToken GenerateAccessToken(User user, DateTimeOffset now);
}

public sealed class GeneratedAccessToken
{
    public required string Token { get; init; }

    public required int ExpiresInSeconds { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}
```

***

## 5A.2 Final `AuthService.cs`

Pakai versi ini, bukan versi di atas.

```csharp
using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Application.Services;

public sealed class AuthService : IAuthService
{
    private static readonly string DummyBcryptHash =
        "$2a$10$7EqJtq98hPqEX7fNZaFWoOHiV9TbhbcM9E4s5f8IC0LObDJ1vQ7iK";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IClock _clock;
    private readonly IValidator<LoginCommand> _validator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IClock clock,
        IValidator<LoginCommand> validator,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _clock = clock;
        _validator = validator;
        _logger = logger;
    }

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
            _logger.LogWarning(
                "Login failed for username {Username}. Reason={Reason}",
                normalizedUsername,
                "InvalidCredentials");

            throw UnauthorizedAppException.InvalidCredentials();
        }

        if (!user.IsActive)
        {
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
}
```

***

# 6. Application Dependency Injection

## `DependencyInjection.cs`

Replace:

```text
src/OrderManagement.Application/DependencyInjection.cs
```

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.DTOs.Auth;
using OrderManagement.Application.Services;
using OrderManagement.Application.Validators.Auth;

namespace OrderManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<IValidator<LoginCommand>, LoginCommandValidator>();

        return services;
    }
}
```

***

# 7. Infrastructure Security

## 7.1 `BCryptPasswordHasher.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Security/BCryptPasswordHasher.cs
```

```csharp
using OrderManagement.Application.Abstractions.Authentication;

namespace OrderManagement.Infrastructure.Security;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch
        {
            return false;
        }
    }
}
```

***

## 7.2 `JwtTokenGenerator.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Security/JwtTokenGenerator.cs
```

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.Security;

public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _jwtOptions;

    public JwtTokenGenerator(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public GeneratedAccessToken GenerateAccessToken(User user, DateTimeOffset now)
    {
        ValidateOptions();

        var expiresAt = now.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new("username", user.Username),
            new("displayName", user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("role", user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new GeneratedAccessToken
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresInSeconds = _jwtOptions.AccessTokenExpirationMinutes * 60,
            ExpiresAt = expiresAt
        };
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_jwtOptions.Issuer))
        {
            throw new InvalidOperationException("JWT issuer is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_jwtOptions.Audience))
        {
            throw new InvalidOperationException("JWT audience is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_jwtOptions.Secret))
        {
            throw new InvalidOperationException("JWT secret is not configured.");
        }

        if (Encoding.UTF8.GetByteCount(_jwtOptions.Secret) < 32)
        {
            throw new InvalidOperationException("JWT secret must be at least 32 bytes.");
        }

        if (_jwtOptions.AccessTokenExpirationMinutes <= 0)
        {
            throw new InvalidOperationException("JWT access token expiration must be greater than zero.");
        }
    }
}
```

***

## 7.3 `CurrentUserContext.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Security/CurrentUserContext.cs
```

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Security;

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

    public bool IsAdminOrOps()
    {
        return Role is UserRole.Admin or UserRole.Ops;
    }
}
```

***

# 8. UserRepository with Dapper

## `UserRepository.cs`

Replace:

```text
src/OrderManagement.Infrastructure/Repositories/UserRepository.cs
```

```csharp
using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               username AS Username,
                               password_hash AS PasswordHash,
                               display_name AS DisplayName,
                               role AS Role,
                               is_active AS IsActive,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM users
                           WHERE id = @Id
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            new CommandDefinition(
                sql,
                new { Id = id },
                cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    public async Task<User?> GetByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               username AS Username,
                               password_hash AS PasswordHash,
                               display_name AS DisplayName,
                               role AS Role,
                               is_active AS IsActive,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM users
                           WHERE lower(username) = lower(@Username)
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            new CommandDefinition(
                sql,
                new { Username = username.Trim() },
                cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    private sealed class UserRow
    {
        public Guid Id { get; init; }

        public string Username { get; init; } = string.Empty;

        public string PasswordHash { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public User ToDomain()
        {
            if (!Enum.TryParse<UserRole>(Role, ignoreCase: true, out var parsedRole))
            {
                throw new InvalidOperationException($"Invalid user role value '{Role}' in database.");
            }

            return User.Rehydrate(
                Id,
                Username,
                PasswordHash,
                DisplayName,
                parsedRole,
                IsActive,
                CreatedAt,
                UpdatedAt);
        }
    }
}
```

***

# 9. Infrastructure Dependency Injection Update

## `DependencyInjection.cs`

Replace:

```text
src/OrderManagement.Infrastructure/DependencyInjection.cs
```

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Infrastructure.Database;
using OrderManagement.Infrastructure.Options;
using OrderManagement.Infrastructure.Repositories;
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

        services.AddHttpContextAccessor();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();

        services.AddScoped<IDatabaseMigrationRunner, DatabaseMigrationRunner>();

        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}
```

***

# 10. API Auth Contracts

## 10.1 `LoginRequest.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Auth/LoginRequest.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Auth;

public sealed class LoginRequest
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
```

***

## 10.2 `AuthenticatedUserResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Auth/AuthenticatedUserResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Auth;

public sealed class AuthenticatedUserResponse
{
    public Guid Id { get; init; }

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;
}
```

***

## 10.3 `LoginResponse.cs`

Replace:

```text
src/OrderManagement.Api/Contracts/Auth/LoginResponse.cs
```

```csharp
namespace OrderManagement.Api.Contracts.Auth;

public sealed class LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;

    public int ExpiresIn { get; init; }

    public AuthenticatedUserResponse User { get; init; } = new();
}
```

***

# 11. AuthController

## `AuthController.cs`

Replace:

```text
src/OrderManagement.Api/Controllers/AuthController.cs
```

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Auth;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.DTOs.Auth;

namespace OrderManagement.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
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
}
```

***

# 12. API Authorization Policy Constants

Create file:

```text
src/OrderManagement.Api/Extensions/AuthorizationPolicies.cs
```

```csharp
namespace OrderManagement.Api.Extensions;

public static class AuthorizationPolicies
{
    public const string CustomerOnly = "CustomerOnly";
    public const string AdminOnly = "AdminOnly";
    public const string OpsOnly = "OpsOnly";
    public const string AdminOrOps = "AdminOrOps";
    public const string CustomerOrAdmin = "CustomerOrAdmin";
    public const string AuthenticatedUser = "AuthenticatedUser";
}
```

***

# 13. AuthenticationExtensions

## `AuthenticationExtensions.cs`

Replace:

```text
src/OrderManagement.Api/Extensions/AuthenticationExtensions.cs
```

```csharp
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
```

> `RequireHttpsMetadata = false` aman untuk local JWT bearer validation karena kita tidak call authority metadata. Kalau nanti OAuth/OIDC production, ini wajib true.

***

# 14. AuthorizationExtensions

## `AuthorizationExtensions.cs`

Replace:

```text
src/OrderManagement.Api/Extensions/AuthorizationExtensions.cs
```

```csharp
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
                AuthorizationPolicies.CustomerOnly,
                policy => policy.RequireRole(UserRole.Customer.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(UserRole.Admin.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.OpsOnly,
                policy => policy.RequireRole(UserRole.Ops.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.AdminOrOps,
                policy => policy.RequireRole(
                    UserRole.Admin.ToString(),
                    UserRole.Ops.ToString()));

            options.AddPolicy(
                AuthorizationPolicies.CustomerOrAdmin,
                policy => policy.RequireRole(
                    UserRole.Customer.ToString(),
                    UserRole.Admin.ToString()));
        });

        return services;
    }
}
```

***

# 15. Program.cs Auth Update

Replace:

```text
src/OrderManagement.Api/Program.cs
```

Dengan:

```csharp
using OrderManagement.Api.Extensions;
using OrderManagement.Api.Options;
using OrderManagement.Application;
using OrderManagement.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.Configure<ClientCorsOptions>(
    builder.Configuration.GetSection(ClientCorsOptions.SectionName));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiAuthorization();

builder.Services.AddCors(options =>
{
    var corsOptions = builder.Configuration
        .GetSection(ClientCorsOptions.SectionName)
        .Get<ClientCorsOptions>() ?? new ClientCorsOptions();

    options.AddPolicy("ClientApps", policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsOptions.AllowedOrigins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy
            .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-ID")
            .WithExposedHeaders("X-Correlation-ID", "Location");
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

await app.ApplyDatabaseMigrationsAsync();

app.UseApiMiddlewares();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        if (httpContext.Items.TryGetValue("CorrelationId", out var correlationId))
        {
            diagnosticContext.Set("CorrelationId", correlationId);
        }

        diagnosticContext.Set("RequestPath", httpContext.Request.Path.Value);
        diagnosticContext.Set("HttpMethod", httpContext.Request.Method);
    };
});

app.UseHttpsRedirection();

app.UseCors("ClientApps");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

app.Run();

public partial class Program;
```

***

# 16. Important Fix: Application Should Not Depend on Infrastructure

Pastikan file `AuthService.cs` **tidak ada** using ini:

```csharp
using Microsoft.Extensions.Options;
using OrderManagement.Infrastructure.Options;
```

Kalau ada, hapus. Kita pakai final version di section **5A.2**.

***

# 17. Optional: Protected Diagnostics Endpoint

Kalau lu masih punya `DiagnosticsController`, bisa tambahin endpoint protected buat test token.

```csharp
[Authorize]
[HttpGet("me")]
public IActionResult Me()
{
    return Ok(new
    {
        userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
        username = User.FindFirst("username")?.Value,
        role = User.FindFirst("role")?.Value
    });
}
```

Kalau mau bersih, skip dulu.

***

# 18. Build

Run:

```bash
dotnet build
```

Kalau ada error package missing:

```bash
dotnet add src/OrderManagement.Api/OrderManagement.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj package System.IdentityModel.Tokens.Jwt
dotnet add src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj package BCrypt.Net-Next
```

Lalu:

```bash
dotnet restore
dotnet build
```

***

# 19. Seed Users

Kalau belum seed:

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -f db/seed/001_seed_users.sql
```

Cek:

```bash
PGPASSWORD=order_password psql -h localhost -p 5432 -U order_user -d order_management -c "SELECT username, role, is_active FROM users;"
```

***

# 20. Run API

```bash
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj
```

Test login:

```bash
curl -i -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: test-login-001" \
  -d '{"username":"admin","password":"Password123!"}'
```

Kalau app redirect ke HTTPS, pakai URL yang keluar di console, misalnya:

```bash
curl -k -i -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -H "X-Correlation-ID: test-login-001" \
  -d '{"username":"admin","password":"Password123!"}'
```

Expected:

```json
{
  "accessToken": "...",
  "expiresIn": 3600,
  "user": {
    "id": "...",
    "username": "admin",
    "displayName": "System Admin",
    "role": "Admin"
  }
}
```

Invalid login:

```bash
curl -k -i -X POST https://localhost:7000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"wrong"}'
```

Expected:

```json
{
  "error": {
    "code": "INVALID_CREDENTIALS",
    "message": "Invalid username or password.",
    "details": [],
    "correlationId": "...",
    "timestamp": "..."
  }
}
```

***

# 21. Commit Batch 4

```bash
git add .
git commit -m "feat: add JWT authentication and login flow"
```

***
