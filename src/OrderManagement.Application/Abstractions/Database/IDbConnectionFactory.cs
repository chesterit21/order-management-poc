using System.Data.Common;

namespace OrderManagement.Application.Abstractions.Database;

public interface IDbConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}