namespace OrderManagement.Application.DTOs.Auth;

public sealed record LoginCommand
{
    public required string Username { get; init; }

    public required string Password { get; init; }
}