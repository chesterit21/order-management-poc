using System.Data.Common;
using Microsoft.Extensions.Options;
using Npgsql;
using OrderManagement.Application.Abstractions.Database;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.Database;

public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseOptions _databaseOptions;

    public DbConnectionFactory(IOptions<DatabaseOptions> databaseOptions)
    {
        _databaseOptions = databaseOptions.Value;
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_databaseOptions.ConnectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        var connection = new NpgsqlConnection(_databaseOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }
}