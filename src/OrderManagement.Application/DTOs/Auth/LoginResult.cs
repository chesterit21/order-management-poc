namespace OrderManagement.Application.DTOs.Auth;

public sealed record LoginResult
{
    public required string AccessToken { get; init; }

    public required int ExpiresIn { get; init; }

    public required AuthenticatedUserResult User { get; init; }
}

public sealed record AuthenticatedUserResult
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string Role { get; init; }
}