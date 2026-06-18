using Dapper;
using Npgsql;

namespace OrderManagement.IntegrationTests.Helpers;

public static class DatabaseAssertHelper
{
    public static async Task<int> GetProductStockAsync(
        string connectionString,
        Guid productId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT stock_quantity
            FROM products
            WHERE id = @ProductId;
            """,
            new { ProductId = productId });
    }

    public static async Task<int> CountOrdersAsync(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM orders;");
    }

    public static async Task<int> CountPaidPaymentsAsync(
        string connectionString,
        Guid orderId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM payments
            WHERE order_id = @OrderId
              AND status = 'Paid';
            """,
            new { OrderId = orderId });
    }

    public static async Task<string> GetOrderStatusAsync(
        string connectionString,
        Guid orderId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<string>(
            """
            SELECT status
            FROM orders
            WHERE id = @OrderId;
            """,
            new { OrderId = orderId });
    }

    public static async Task<long> GetOrderRowVersionAsync(
        string connectionString,
        Guid orderId)
    {
        await using var connection = new NpgsqlConnection(connectionString);

        return await connection.ExecuteScalarAsync<long>(
            """
            SELECT row_version
            FROM orders
            WHERE id = @OrderId;
            """,
            new { OrderId = orderId });
    }
}