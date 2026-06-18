namespace OrderManagement.Api.Contracts.Auth;

public sealed record AuthenticatedUserResponse
{
    public Guid Id { get; init; }

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;
}