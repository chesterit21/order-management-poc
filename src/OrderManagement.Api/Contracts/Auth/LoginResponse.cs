namespace OrderManagement.Api.Contracts.Auth;

public sealed record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;

    public int ExpiresIn { get; init; }

    public AuthenticatedUserResponse User { get; init; } = new();
}