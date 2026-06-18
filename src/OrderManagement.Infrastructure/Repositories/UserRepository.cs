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