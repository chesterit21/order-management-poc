using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public sealed class User : AuditableEntity
{
    private User()
    {
    }

    private User(
        Guid id,
        string username,
        string passwordHash,
        string displayName,
        UserRole role,
        bool isActive,
        DateTimeOffset createdAt)
        : base(id)
    {
        Username = username;
        PasswordHash = passwordHash;
        DisplayName = displayName;
        Role = role;
        IsActive = isActive;
        SetCreatedAt(createdAt);
    }

    public string Username { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public static User Create(
        string username,
        string passwordHash,
        string displayName,
        UserRole role,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        return new User(
            Guid.NewGuid(),
            username.Trim(),
            passwordHash,
            displayName.Trim(),
            role,
            true,
            now);
    }

    public static User Rehydrate(
        Guid id,
        string username,
        string passwordHash,
        string displayName,
        UserRole role,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var user = new User(
            id,
            username,
            passwordHash,
            displayName,
            role,
            isActive,
            createdAt);

        user.SetUpdatedAt(updatedAt);

        return user;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        SetUpdatedAt(now);
    }

    public bool HasRole(UserRole role)
    {
        return Role == role;
    }

    public bool IsAdminOrOps()
    {
        return Role is UserRole.ApplicationAdmin; // Only ApplicationAdmin can perform admin/ops functions now
    }
}