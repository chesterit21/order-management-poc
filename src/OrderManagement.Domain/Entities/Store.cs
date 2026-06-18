using OrderManagement.Domain.Common;

namespace OrderManagement.Domain.Entities;

public sealed class Store : AuditableEntity
{
    private Store()
    {
    }

    private Store(
        Guid id,
        Guid ownerUserId,
        string storeName,
        string slug,
        string? description,
        string? logoUrl,
        bool isActive,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        if (string.IsNullOrWhiteSpace(storeName))
        {
            throw new ArgumentException("Store name is required.", nameof(storeName));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Store slug is required.", nameof(slug));
        }

        OwnerUserId = ownerUserId;
        StoreName = storeName.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        IsActive = isActive;
        SetCreatedAt(createdAt);
    }

    public Guid OwnerUserId { get; private set; }

    public string StoreName { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? LogoUrl { get; private set; }

    public bool IsActive { get; private set; }

    public static Store Create(
        Guid ownerUserId,
        string storeName,
        string slug,
        string? description,
        DateTimeOffset now)
    {
        return new Store(
            Guid.NewGuid(),
            ownerUserId,
            storeName,
            slug,
            description,
            null,
            true,
            now);
    }

    public static Store Rehydrate(
        Guid id,
        Guid ownerUserId,
        string storeName,
        string slug,
        string? description,
        string? logoUrl,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var store = new Store(
            id,
            ownerUserId,
            storeName,
            slug,
            description,
            logoUrl,
            isActive,
            createdAt);

        store.SetUpdatedAt(updatedAt);

        return store;
    }
}