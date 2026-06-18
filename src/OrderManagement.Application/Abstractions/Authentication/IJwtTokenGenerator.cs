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