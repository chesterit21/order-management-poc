using Dapper;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Products;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResult<ProductDto>> ListAsync(
        ProductListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var offset = (query.Page - 1) * query.PageSize;
        var search = NormalizeSearch(query.Search);

        var whereClause = search is null
            ? "WHERE p.is_active = TRUE"
            : """
              WHERE p.is_active = TRUE
                AND (
                    p.sku ILIKE @Search ESCAPE '\'
                    OR p.name ILIKE @Search ESCAPE '\'
                )
              """;

        var countSql = $"""
                        SELECT COUNT(*)
                        FROM products p
                        LEFT JOIN stores s ON s.id = p.store_id
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
                           p.is_active AS IsActive
                       FROM products p
                       LEFT JOIN stores s ON s.id = p.store_id
                       {whereClause}
                       ORDER BY p.name ASC, p.id ASC
                       LIMIT @PageSize OFFSET @Offset;
                       """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var parameters = new
        {
            Search = search,
            query.PageSize,
            Offset = offset
        };

        var totalItems = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                countSql,
                parameters,
                cancellationToken: cancellationToken));

        var items = await connection.QueryAsync<ProductDto>(
            new CommandDefinition(
                dataSql,
                parameters,
                cancellationToken: cancellationToken));

        return new PagedResult<ProductDto>
        {
            Items = items.AsList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems
        };
    }

    public async Task<ProductDto?> GetDetailByIdAsync(
        Guid id,
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
                               p.is_active AS IsActive
                           FROM products p
                           LEFT JOIN stores s ON s.id = p.store_id
                           WHERE p.id = @Id
                             AND p.is_active = TRUE
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<ProductDto>(
            new CommandDefinition(
                sql,
                new { Id = id },
                cancellationToken: cancellationToken));
    }

    public async Task<Product?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               id AS Id,
                               sku AS Sku,
                               name AS Name,
                               stock_quantity AS StockQuantity,
                               price AS Price,
                               row_version AS RowVersion,
                               is_active AS IsActive,
                               created_at AS CreatedAt,
                               updated_at AS UpdatedAt
                           FROM products
                           WHERE id = @Id
                             AND is_active = TRUE
                           LIMIT 1;
                           """;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<ProductRow>(
            new CommandDefinition(
                sql,
                new { Id = id },
                cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = EscapeLikePattern(search.Trim());

        return $"%{escaped}%";
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

    private sealed class ProductRow
    {
        public Guid Id { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public int StockQuantity { get; init; }

        public decimal Price { get; init; }

        public long RowVersion { get; init; }

        public bool IsActive { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public Product ToDomain()
        {
            return Product.Rehydrate(
                Id,
                Sku,
                Name,
                StockQuantity,
                Price,
                RowVersion,
                IsActive,
                CreatedAt,
                UpdatedAt);
        }
    }
}