namespace OrderManagement.Api.Contracts.Stores;

public sealed record CreateStoreOperatorRequest
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}