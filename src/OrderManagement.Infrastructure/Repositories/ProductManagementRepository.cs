using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products.Backoffice;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class ProductManagementRepository : IProductManagementRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductManagementRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResult<BackofficeProductDto>> ListAsync(
        BackofficeProductListQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        CancellationToken cancellationToken = default)
    {
        var offset = (query.Page - 1) * query.PageSize;

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (allowedStoreIds is not null)
        {
            if (allowedStoreIds.Count == 0)
            {
                return new PagedResult<BackofficeProductDto>
                {
                    Items = [],
                    Page = query.Page,
                    PageSize = query.PageSize,
                    TotalItems = 0
                };
            }

            conditions.Add("p.store_id = ANY(@AllowedStoreIds)");
            parameters.Add("AllowedStoreIds", allowedStoreIds.ToArray());
        }

        if (query.StoreId is not null)
        {
            conditions.Add("p.store_id = @StoreId");
            parameters.Add("StoreId", query.StoreId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("""
                           (
                               p.sku ILIKE @Search ESCAPE '\'
                               OR p.name ILIKE @Search ESCAPE '\'
                           )
                           """);
            parameters.Add("Search", $"%{EscapeLikePattern(query.Search.Trim())}%");
        }

        if (query.IsActive is not null)
        {
            conditions.Add("p.is_active = @IsActive");
            parameters.Add("IsActive", query.IsActive.Value);
        }

        parameters.Add("PageSize", query.PageSize);
        parameters.Add("Offset", offset);

        var whereClause = conditions.Count == 0
            ? string.Empty
            : "WHERE " + string.Join(" AND ", conditions);

        var countSql = $"""
                        SELECT COUNT(*)
                        FROM products p
                        INNER JOIN stores s ON s.id = p.store_id
                        {whereClause};
                        """;

        var dataSql = $"""
                       SELECT
                           p.id AS Id,
                           p.store_id AS StoreId,
                           s.store_name AS StoreName,
                           p.sku AS Sku,
                           p.name AS Name,
                           p.description AS Description,
                           p.primary_image_url AS ImageUrl,
                           p.stock_quantity AS StockQuantity,
                           p.price AS Price,
                           p.row_version AS RowVersion,
                           p.is_active AS IsActive,
                           p.created_at AS CreatedAt,
                           p.updated_at AS UpdatedAt
                       FROM products p
                       INNER JOIN stores s ON s.id = p.store_id
                       {whereClause}
                       ORDER BY p.created_at DESC, p.id DESC
                       LIMIT @PageSize OFFSET @Offset;
                       """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                countSql,
                parameters,
                cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<BackofficeProductDto>(
            new CommandDefinition(
                dataSql,
                parameters,
                cancellationToken: cancellationToken));

        return new PagedResult<BackofficeProductDto>
        {
            Items = items.AsList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<BackofficeProductDto?> GetByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               p.id AS Id,
                               p.store_id AS StoreId,
                               s.store_name AS StoreName,
                               p.sku AS Sku,
                               p.name AS Name,
                               p.description AS Description,
                               p.primary_image_url AS ImageUrl,
                               p.stock_quantity AS StockQuantity,
                               p.price AS Price,
                               p.row_version AS RowVersion,
                               p.is_active AS IsActive,
                               p.created_at AS CreatedAt,
                               p.updated_at AS UpdatedAt
                           FROM products p
                           INNER JOIN stores s ON s.id = p.store_id
                           WHERE p.id = @ProductId
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<BackofficeProductDto>(
            new CommandDefinition(
                sql,
                new { ProductId = productId },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> SkuExistsAsync(
        string sku,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM products
                               WHERE lower(sku) = lower(@Sku)
                           );
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { Sku = sku },
                cancellationToken: cancellationToken));
    }

    public async Task<BackofficeProductDto> CreateAsync(
        CreateProductPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           INSERT INTO products
                               (id, store_id, sku, name, description, primary_image_url,
                                stock_quantity, price, row_version, is_active, created_at, updated_at)
                           VALUES
                               (@ProductId, @StoreId, @Sku, @Name, @Description, NULL,
                                @StockQuantity, @Price, 1, TRUE, @Now, @Now);
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        return await GetByIdAsync(request.ProductId, cancellationToken)
               ?? throw new InvalidOperationException("Created product cannot be found.");
    }

    public async Task<BackofficeProductDto> UpdateAsync(
        UpdateProductPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE products
                           SET
                               name = @Name,
                               description = @Description,
                               price = @Price,
                               row_version = row_version + 1,
                               updated_at = @Now
                           WHERE id = @ProductId
                             AND row_version = @ExpectedRowVersion;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw new ConcurrencyAppException(
                "Product has been modified by another user. Please refresh and try again.");
        }

        return await GetByIdAsync(request.ProductId, cancellationToken)
               ?? throw new InvalidOperationException("Updated product cannot be found.");
    }

    public async Task<BackofficeProductDto> SetStatusAsync(
        SetProductStatusPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE products
                           SET
                               is_active = @IsActive,
                               row_version = row_version + 1,
                               updated_at = @Now
                           WHERE id = @ProductId
                             AND row_version = @ExpectedRowVersion;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw new ConcurrencyAppException(
                "Product has been modified by another user. Please refresh and try again.");
        }

        return await GetByIdAsync(request.ProductId, cancellationToken)
               ?? throw new InvalidOperationException("Updated product cannot be found.");
    }

    public async Task<BackofficeProductDto> UpdateImageAsync(
        UpdateProductImagePersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           UPDATE products
                           SET
                               primary_image_url = @ImageUrl,
                               row_version = row_version + 1,
                               updated_at = @Now
                           WHERE id = @ProductId;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                request,
                cancellationToken: cancellationToken));

        if (affected != 1)
        {
            throw NotFoundAppException.Product(request.ProductId);
        }

        return await GetByIdAsync(request.ProductId, cancellationToken)
               ?? throw new InvalidOperationException("Updated product cannot be found.");
    }

    public async Task<AdjustProductStockResult> AdjustStockAsync(
        AdjustProductStockPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE products
            SET
                stock_quantity = CASE
                    WHEN @AdjustmentType = 'Increase' THEN stock_quantity + @Quantity
                    WHEN @AdjustmentType = 'Decrease' THEN GREATEST(0, stock_quantity - @Quantity)
                    WHEN @AdjustmentType = 'Set' THEN @Quantity
                    ELSE stock_quantity
                END,
                row_version = row_version + 1,
                updated_at = @Now
            WHERE id = @ProductId
              AND row_version = @ExpectedRowVersion
            RETURNING
                id AS ProductId,
                store_id AS StoreId,
                sku AS Sku,
                name AS Name,
                @AdjustmentType AS AdjustmentType,
                @Quantity AS Quantity,
                stock_quantity - CASE
                    WHEN @AdjustmentType = 'Increase' THEN @Quantity
                    WHEN @AdjustmentType = 'Decrease' THEN -@Quantity
                    WHEN @AdjustmentType = 'Set' THEN @Quantity - stock_quantity
                    ELSE 0
                END AS StockBefore,
                stock_quantity AS StockAfter,
                row_version AS RowVersion,
                updated_at AS UpdatedAt
        ";

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var result = await connection.QuerySingleOrDefaultAsync<AdjustProductStockResult>(
            new CommandDefinition(
                sql,
                new
                {
                    request.ProductId,
                    AdjustmentType = request.AdjustmentType.ToString(),
                    request.Quantity,
                    request.ExpectedRowVersion,
                    request.Now
                },
                cancellationToken: cancellationToken));

        if (result is null)
        {
            throw new ConcurrencyAppException(
                "Product has been modified by another user. Please refresh and try again.");
        }

        return result;
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }
}