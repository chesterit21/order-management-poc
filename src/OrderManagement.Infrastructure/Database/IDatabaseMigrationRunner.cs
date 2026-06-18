namespace OrderManagement.Infrastructure.Database;

public interface IDatabaseMigrationRunner
{
    Task ApplyMigrationsAsync(CancellationToken cancellationToken = default);
}