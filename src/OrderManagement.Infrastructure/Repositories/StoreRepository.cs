using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Constants;
using OrderManagement.Application.DTOs.Stores;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class StoreRepository : IStoreRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StoreRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> UserHasOwnedStoreAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM stores
                               WHERE owner_user_id = @OwnerUserId
                           );
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { OwnerUserId = ownerUserId },
                cancellationToken: cancellationToken));
    }

    public async Task<StoreDto?> GetByIdAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               owner_user_id AS OwnerUserId,
                               store_name AS StoreName,
                               slug AS Slug,
                               description AS Description,
                               logo_url AS LogoUrl,
                               is_active AS IsActive,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM stores
                           WHERE id = @StoreId
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<StoreDto>(
            new CommandDefinition(
                sql,
                new { StoreId = storeId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyCollection<StoreDto>> ListByUserMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               s.id AS Id,
                               s.owner_user_id AS OwnerUserId,
                               s.store_name AS StoreName,
                               s.slug AS Slug,
                               s.description AS Description,
                               s.logo_url AS LogoUrl,
                               s.is_active AS IsActive,
                               s.created_at AS CreatedAt,
                               s.updated_at AS UpdatedAt
                           FROM stores s
                           INNER JOIN store_members sm ON sm.store_id = s.id
                           WHERE sm.user_id = @UserId
                             AND sm.is_active = TRUE
                           ORDER BY s.created_at DESC, s.id DESC;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<StoreDto>(
            new CommandDefinition(
                sql,
                new { UserId = userId },
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<IReadOnlyCollection<StoreDto>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               owner_user_id AS OwnerUserId,
                               store_name AS StoreName,
                               slug AS Slug,
                               description AS Description,
                               logo_url AS LogoUrl,
                               is_active AS IsActive,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM stores
                           ORDER BY created_at DESC, id DESC;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<StoreDto>(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<bool> IsStoreOwnerAsync(
        Guid storeId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM store_members
                               WHERE store_id = @StoreId
                                 AND user_id = @UserId
                                 AND role = @Role
                                 AND is_active = TRUE
                           );
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    StoreId = storeId,
                    UserId = userId,
                    Role = StoreMemberRole.Owner.ToString()
                },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> IsStoreOperatorAsync(
        Guid storeId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM store_members
                               WHERE store_id = @StoreId
                                 AND user_id = @UserId
                                 AND role = @Role
                                 AND is_active = TRUE
                           );
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new
                {
                    StoreId = storeId,
                    UserId = userId,
                    Role = StoreMemberRole.Operator.ToString()
                },
                cancellationToken: cancellationToken));
    }

    public async Task<StoreDto> OpenStoreAsync(
        OpenStorePersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO stores
                        (id, owner_user_id, store_name, slug, description, logo_url, is_active, created_at, updated_at)
                    VALUES
                        (@StoreId, @OwnerUserId, @StoreName, @Slug, @Description, NULL, TRUE, @Now, @Now);
                    """,
                    request,
                    transaction,
                    cancellationToken: cancellationToken));

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO store_members
                        (id, store_id, user_id, role, is_active, created_by, created_at, updated_at)
                    VALUES
                        (@Id, @StoreId, @UserId, @Role, TRUE, @CreatedBy, @Now, @Now);
                    """,
                    new
                    {
                        Id = Guid.NewGuid(),
                        request.StoreId,
                        UserId = request.OwnerUserId,
                        Role = StoreMemberRole.Owner.ToString(),
                        CreatedBy = request.OwnerUserId,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE users
                    SET
                        role = @SellerAdminRole,
                        updated_at = @Now
                    WHERE id = @OwnerUserId
                      AND role IN (@BuyerRole, @SellerAdminRole);
                    """,
                    new
                    {
                        SellerAdminRole = UserRole.SellerAdmin.ToString(),
                        BuyerRole = UserRole.Buyer.ToString(),
                        request.OwnerUserId,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            var store = await GetByIdAsync(request.StoreId, cancellationToken);

            return store ?? throw new InvalidOperationException("Created store cannot be found.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<StoreDto> UpdateStoreAsync(
        UpdateStorePersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE stores
                           SET
                               store_name = @StoreName,
                               description = @Description,
                               updated_at = @Now
                           WHERE id = @StoreId;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw new NotFoundAppException(
                "Store was not found.",
                ErrorCodes.StoreNotFound);
        }

        var store = await GetByIdAsync(request.StoreId, cancellationToken);

        return store ?? throw new InvalidOperationException("Updated store cannot be found.");
    }

    public async Task<IReadOnlyCollection<StoreMemberDto>> ListOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               sm.id AS Id,
                               sm.store_id AS StoreId,
                               sm.user_id AS UserId,
                               u.username AS Username,
                               u.display_name AS DisplayName,
                               sm.role AS Role,
                               sm.is_active AS IsActive,
                               sm.created_at AS CreatedAt,
                               sm.updated_at AS UpdatedAt
                           FROM store_members sm
                           INNER JOIN users u ON u.id = sm.user_id
                           WHERE sm.store_id = @StoreId
                             AND sm.role = @Role
                           ORDER BY sm.created_at DESC, sm.id DESC;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<StoreMemberDto>(
            new CommandDefinition(
                sql,
                new
                {
                    StoreId = storeId,
                    Role = StoreMemberRole.Operator.ToString()
                },
                cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<StoreMemberDto> CreateOperatorAsync(
        CreateStoreOperatorPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var usernameExists = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM users
                        WHERE lower(username) = lower(@Username)
                    );
                    """,
                    new { request.Username },
                    transaction,
                    cancellationToken: cancellationToken));

            if (usernameExists)
            {
                throw new ConflictAppException(
                    ErrorCodes.UserAlreadyExists,
                    "Username already exists.");
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO users
                        (id, username, password_hash, display_name, role, is_active, created_at, updated_at)
                    VALUES
                        (@OperatorUserId, @Username, @PasswordHash, @DisplayName, @Role, TRUE, @Now, @Now);
                    """,
                    new
                    {
                        request.OperatorUserId,
                        request.Username,
                        request.PasswordHash,
                        request.DisplayName,
                        Role = UserRole.SellerOperator.ToString(),
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            var memberId = Guid.NewGuid();

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO store_members
                        (id, store_id, user_id, role, is_active, created_by, created_at, updated_at)
                    VALUES
                        (@Id, @StoreId, @UserId, @Role, TRUE, @CreatedBy, @Now, @Now);
                    """,
                    new
                    {
                        Id = memberId,
                        request.StoreId,
                        UserId = request.OperatorUserId,
                        Role = StoreMemberRole.Operator.ToString(),
                        request.CreatedBy,
                        request.Now
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            return await GetStoreMemberAsync(memberId, cancellationToken)
                   ?? throw new InvalidOperationException("Created store operator cannot be found.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<StoreMemberDto> SetOperatorStatusAsync(
        SetStoreOperatorStatusPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE store_members
                           SET
                               is_active = @IsActive,
                               updated_at = @Now
                           WHERE store_id = @StoreId
                             AND user_id = @OperatorUserId
                             AND role = @Role
                           RETURNING id;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var memberId = await connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                sql,
                new
                {
                    request.StoreId,
                    request.OperatorUserId,
                    request.IsActive,
                    request.Now,
                    Role = StoreMemberRole.Operator.ToString()
                },
                cancellationToken: cancellationToken));

        if (memberId is null)
        {
            throw new NotFoundAppException(
                "Store operator was not found.",
                ErrorCodes.UserNotFound);
        }

        return await GetStoreMemberAsync(memberId.Value, cancellationToken)
               ?? throw new InvalidOperationException("Updated store operator cannot be found.");
    }

    private async Task<StoreMemberDto?> GetStoreMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               sm.id AS Id,
                               sm.store_id AS StoreId,
                               sm.user_id AS UserId,
                               u.username AS Username,
                               u.display_name AS DisplayName,
                               sm.role AS Role,
                               sm.is_active AS IsActive,
                               sm.created_at AS CreatedAt,
                               sm.updated_at AS UpdatedAt
                           FROM store_members sm
                           INNER JOIN users u ON u.id = sm.user_id
                           WHERE sm.id = @MemberId
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<StoreMemberDto>(
            new CommandDefinition(
                sql,
                new { MemberId = memberId },
                cancellationToken: cancellationToken));
    }
}