using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public sealed class StoreMember : AuditableEntity
{
    private StoreMember()
    {
    }

    private StoreMember(
        Guid id,
        Guid storeId,
        Guid userId,
        StoreMemberRole role,
        bool isActive,
        Guid createdBy,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("Store id is required.", nameof(storeId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (createdBy == Guid.Empty)
        {
            throw new ArgumentException("Created by is required.", nameof(createdBy));
        }

        StoreId = storeId;
        UserId = userId;
        Role = role;
        IsActive = isActive;
        CreatedBy = createdBy;
        SetCreatedAt(createdAt);
    }

    public Guid StoreId { get; private set; }

    public Guid UserId { get; private set; }

    public StoreMemberRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedBy { get; private set; }

    public static StoreMember CreateOwner(
        Guid storeId,
        Guid userId,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new StoreMember(
            Guid.NewGuid(),
            storeId,
            userId,
            StoreMemberRole.Owner,
            true,
            createdBy,
            now);
    }

    public static StoreMember CreateOperator(
        Guid storeId,
        Guid userId,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new StoreMember(
            Guid.NewGuid(),
            storeId,
            userId,
            StoreMemberRole.Operator,
            true,
            createdBy,
            now);
    }

    public static StoreMember Rehydrate(
        Guid id,
        Guid storeId,
        Guid userId,
        StoreMemberRole role,
        bool isActive,
        Guid createdBy,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var member = new StoreMember(
            id,
            storeId,
            userId,
            role,
            isActive,
            createdBy,
            createdAt);

        member.SetUpdatedAt(updatedAt);

        return member;
    }
}